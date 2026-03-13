using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using TalentSuite.Server.Bids;
using TalentSuite.Server.Health;
using TalentSuite.Server.Messaging;
using TalentSuite.Server.Security;
using TalentSuite.Server.Users;
using TalentSuite.Server.Users.Seeding;
using TalentSuite.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
var authenticationEnabled = IsAuthenticationEnabled(builder.Configuration);

builder.AddServiceDefaults();

// API Controllers (instead of Minimal API)
builder.Services.AddControllers();

// Optional: keep Razor Pages for the default Error page
builder.Services.AddRazorPages();

// Ingestion service
builder.Services.AddBidServices(builder.Configuration);
builder.Services.AddUserServices(builder.Configuration);
builder.Services.AddAzureServiceBusMessaging(builder.Configuration);
builder.Services.AddScoped<IHealthCheckProbe, SqlDatabaseHealthCheckProbe>();

var keycloakAuthority = Environment.GetEnvironmentVariable("KEYCLOAK_AUTHORITY")
    ?? BuildKeycloakAuthorityFromEndpointVariables()
    ?? builder.Configuration["KEYCLOAK_AUTHORITY"]
    ?? "http://localhost:8080/realms/TalentConsulting";
var keycloakAudience = Environment.GetEnvironmentVariable("KEYCLOAK_AUDIENCE")
    ?? builder.Configuration["KEYCLOAK_AUDIENCE"]
    ?? "talentsuite-server";

if (authenticationEnabled)
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.Authority = keycloakAuthority;
            options.Audience = keycloakAudience;
            options.RequireHttpsMetadata = !keycloakAuthority.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
            options.TokenValidationParameters.ValidateAudience = false;
            options.TokenValidationParameters.ValidateIssuer = !builder.Environment.IsDevelopment();
            options.TokenValidationParameters.RoleClaimType = ClaimTypes.Role;

            if (builder.Environment.IsDevelopment())
            {
                options.BackchannelHttpHandler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback =
                        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                };
            }

            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    var logger = context.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("Auth.JwtBearer");
                    logger.LogWarning(context.Exception, "JWT authentication failed.");
                    return Task.CompletedTask;
                },
                OnChallenge = context =>
                {
                    var logger = context.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("Auth.JwtBearer");
                    logger.LogWarning("JWT challenge issued. Error: {Error}, Description: {Description}",
                        context.Error,
                        context.ErrorDescription);
                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    if (context.Principal?.Identity is not ClaimsIdentity identity)
                        return Task.CompletedTask;

                    AddRealmRoles(identity, context.Principal);
                    AddResourceRoles(identity, context.Principal, keycloakAudience);

                    return Task.CompletedTask;
                }
            };
        });

}
else
{
    builder.Services.AddAuthentication(DevelopmentBypassAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, DevelopmentBypassAuthenticationHandler>(
            DevelopmentBypassAuthenticationHandler.SchemeName,
            _ => { });
}

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdminRole", policy => policy.RequireRole("admin", "Admin"));
    options.AddPolicy("RequireBidAccess", policy =>
        policy.RequireAuthenticatedUser().AddRequirements(new BidAccessRequirement()));
});
builder.Services.AddScoped<IAuthorizationHandler, BidAccessAuthorizationHandler>();

var apiBaseAddress = Environment.GetEnvironmentVariable("TALENTFRONTEND_HTTPS")
                                                         ?? builder.Configuration["TALENTFRONTEND_HTTPS"]
                                                         ?? "https+http://talentfrontend";
var additionalFrontendOrigin = Environment.GetEnvironmentVariable("FRONTEND_PUBLIC_ORIGIN")
                               ?? builder.Configuration["FRONTEND_PUBLIC_ORIGIN"];
// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("BlazorDev", policy =>
        policy.SetIsOriginAllowed(origin =>
            IsAllowedFrontendOrigin(origin, apiBaseAddress, additionalFrontendOrigin))
            .AllowAnyHeader()
            .AllowAnyMethod()
    );
});
builder.Services.AddBidMappings();
builder.Services.AddUserMappings();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseCors("BlazorDev");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapRazorPages();
app.MapFallbackToFile("index.html");
app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

await app.SeedUsersAsync();

app.Run();

static bool IsAuthenticationEnabled(IConfiguration configuration)
{
    var raw = Environment.GetEnvironmentVariable("AUTHENTICATION_ENABLED")
        ?? configuration["AUTHENTICATION_ENABLED"]
        ?? "true";

    return !string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase);
}

static string? BuildKeycloakAuthorityFromEndpointVariables()
{
    var keycloakBaseAddress = Environment.GetEnvironmentVariable("KEYCLOAK_HTTPS")
                              ?? Environment.GetEnvironmentVariable("KEYCLOAK_HTTP");
    if (string.IsNullOrWhiteSpace(keycloakBaseAddress))
        return null;

    return $"{keycloakBaseAddress.TrimEnd('/')}/realms/TalentConsulting";
}

static void AddRealmRoles(ClaimsIdentity identity, ClaimsPrincipal principal)
{
    var realmAccess = principal.FindFirst("realm_access")?.Value;
    if (string.IsNullOrWhiteSpace(realmAccess))
        return;

    using var document = JsonDocument.Parse(realmAccess);
    if (!document.RootElement.TryGetProperty("roles", out var roles) || roles.ValueKind != JsonValueKind.Array)
        return;

    foreach (var role in roles.EnumerateArray())
    {
        AddRoleClaim(identity, role.GetString());
    }
}

static void AddResourceRoles(ClaimsIdentity identity, ClaimsPrincipal principal, string audience)
{
    var resourceAccess = principal.FindFirst("resource_access")?.Value;
    if (string.IsNullOrWhiteSpace(resourceAccess))
        return;

    using var document = JsonDocument.Parse(resourceAccess);
    if (!document.RootElement.TryGetProperty(audience, out var audienceNode))
        return;

    if (!audienceNode.TryGetProperty("roles", out var roles) || roles.ValueKind != JsonValueKind.Array)
        return;

    foreach (var role in roles.EnumerateArray())
    {
        AddRoleClaim(identity, role.GetString());
    }
}

static void AddRoleClaim(ClaimsIdentity identity, string? role)
{
    if (string.IsNullOrWhiteSpace(role))
        return;

    if (identity.HasClaim(ClaimTypes.Role, role))
        return;

    identity.AddClaim(new Claim(ClaimTypes.Role, role));
}

static bool IsAllowedFrontendOrigin(string? origin, string configuredOrigin, string? additionalOrigin)
{
    if (string.IsNullOrWhiteSpace(origin))
        return false;

    if (string.Equals(origin, configuredOrigin, StringComparison.OrdinalIgnoreCase))
        return true;

    if (!string.IsNullOrWhiteSpace(additionalOrigin)
        && string.Equals(origin, additionalOrigin, StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
        return false;

    return originUri.Scheme == Uri.UriSchemeHttps
           && originUri.Host.EndsWith(".z33.web.core.windows.net", StringComparison.OrdinalIgnoreCase);
}

namespace TalentSuite.Server
{
    public partial class Program;
}
