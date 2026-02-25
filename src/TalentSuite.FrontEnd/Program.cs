using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using TalentSuite.FrontEnd;
using TalentSuite.FrontEnd.Pages;
using TalentSuite.FrontEnd.Pages.Bids;
using TalentSuite.FrontEnd.Pages.Bids.Management;
using TalentSuite.FrontEnd.Security;
using TalentSuite.FrontEnd.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
var strictConfiguration = IsStrictConfiguration(builder.Configuration, builder.HostEnvironment);
var authenticationEnabled = IsAuthenticationEnabled(builder.Configuration, strictConfiguration);
var apiBaseAddress = builder.Configuration["TALENTSERVER_HTTPS"]
    ?? builder.Configuration["TALENTSERVER_HTTP"];
var keycloakAuthority = builder.Configuration["KEYCLOAK_AUTHORITY"]
    ?? BuildKeycloakAuthorityFromEndpointVariables(builder.Configuration);
var keycloakClientId = builder.Configuration["KEYCLOAK_CLIENT_ID"];

if (string.IsNullOrWhiteSpace(apiBaseAddress))
{
    if (!strictConfiguration)
    {
        apiBaseAddress = "https://localhost:5001";
    }
    else
    {
        throw new InvalidOperationException(
            "Missing TALENTSERVER_HTTPS/TALENTSERVER_HTTP configuration for frontend API base address.");
    }
}

if (authenticationEnabled && string.IsNullOrWhiteSpace(keycloakAuthority))
{
    if (!strictConfiguration)
    {
        keycloakAuthority = "http://localhost:8080/realms/TalentConsulting";
    }
    else
    {
        throw new InvalidOperationException(
            "Missing KEYCLOAK_AUTHORITY (or KEYCLOAK_HTTPS/KEYCLOAK_HTTP) configuration for OIDC authority.");
    }
}

if (authenticationEnabled && string.IsNullOrWhiteSpace(keycloakClientId))
{
    if (!strictConfiguration)
    {
        keycloakClientId = "talentsuite-frontend";
    }
    else
    {
        throw new InvalidOperationException(
            "Missing KEYCLOAK_CLIENT_ID configuration for OIDC client id.");
    }
}

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.Services.AddSingleton<BidState>();
builder.Services.AddScoped<GlobalBannerState>();
builder.Services.AddScoped<GlobalLoadingState>();
builder.Services.AddScoped<LoadingHttpMessageHandler>();
builder.Services.AddScoped<BidManageApiClient>();
builder.Services.AddScoped<AcceptInviteApiClient>();
builder.Services.AddScoped<ProfileApiClient>();
builder.Services.AddBidMappings();

builder.Services.AddAuthorizationCore(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("admin", "Admin"));
});

if (authenticationEnabled)
{
    builder.Services.AddScoped<ApiAuthorizationMessageHandler>();
    builder.Services.AddScoped<AccountClaimsPrincipalFactory<RemoteUserAccount>, KeycloakAccountClaimsPrincipalFactory>();
    builder.Services.AddOidcAuthentication(options =>
    {
        options.ProviderOptions.Authority = keycloakAuthority;
        options.ProviderOptions.ClientId = keycloakClientId;
        options.ProviderOptions.ResponseType = "code";
        EnsureScope(options.ProviderOptions.DefaultScopes, "email");
        EnsureScope(options.ProviderOptions.DefaultScopes, "roles");
    });
}

builder.Services.AddScoped(sp =>
{
    var handler = sp.GetRequiredService<LoadingHttpMessageHandler>();
    if (authenticationEnabled)
    {
        var authHandler = sp.GetRequiredService<ApiAuthorizationMessageHandler>();
        authHandler.InnerHandler = new HttpClientHandler();
        handler.InnerHandler = authHandler;
    }
    else
    {
        handler.InnerHandler = new HttpClientHandler();
    }

    return new HttpClient(handler)
    {
        BaseAddress = new Uri(apiBaseAddress)
    };
});

await builder.Build().RunAsync();

static bool IsAuthenticationEnabled(IConfiguration configuration, bool strictConfiguration)
{
    var raw = configuration["AUTHENTICATION_ENABLED"];
    if (string.IsNullOrWhiteSpace(raw))
    {
        if (strictConfiguration)
        {
            throw new InvalidOperationException(
                "Missing AUTHENTICATION_ENABLED configuration.");
        }

        raw = "true";
    }

    return !string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase);
}

static bool IsStrictConfiguration(IConfiguration configuration, IWebAssemblyHostEnvironment hostEnvironment)
{
    var strictFromConfig = configuration["STRICT_CONFIGURATION"];
    if (bool.TryParse(strictFromConfig, out var parsed))
        return parsed;

    return !hostEnvironment.IsDevelopment();
}

static string? BuildKeycloakAuthorityFromEndpointVariables(IConfiguration configuration)
{
    var keycloakBaseAddress = configuration["KEYCLOAK_HTTPS"]
                              ?? configuration["KEYCLOAK_HTTP"];
    if (string.IsNullOrWhiteSpace(keycloakBaseAddress))
        return null;

    return $"{keycloakBaseAddress.TrimEnd('/')}/realms/TalentConsulting";
}

static void EnsureScope(ICollection<string> scopes, string scope)
{
    if (scopes.Any(existing => string.Equals(existing, scope, StringComparison.OrdinalIgnoreCase)))
        return;

    scopes.Add(scope);
}
