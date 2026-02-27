using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using TalentSuite.FrontEnd;
using TalentSuite.FrontEnd.Pages;
using TalentSuite.FrontEnd.Pages.Bids;
using TalentSuite.FrontEnd.Pages.Bids.Management;
using TalentSuite.FrontEnd.Configuration;
using TalentSuite.FrontEnd.Security;
using TalentSuite.FrontEnd.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
var strictConfiguration = IsStrictConfiguration(builder.Configuration, builder.HostEnvironment);
var authenticationEnabled = FrontendConfiguration.IsAuthenticationEnabled(builder.Configuration, strictConfiguration);
var apiBaseAddress = FrontendConfiguration.ResolveApiBaseAddress(builder.Configuration, strictConfiguration);
var keycloakAuthority = authenticationEnabled
    ? FrontendConfiguration.ResolveKeycloakAuthority(builder.Configuration, strictConfiguration)
    : null;
var keycloakClientId = authenticationEnabled
    ? FrontendConfiguration.ResolveKeycloakClientId(builder.Configuration, strictConfiguration)
    : null;

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

static bool IsStrictConfiguration(IConfiguration configuration, IWebAssemblyHostEnvironment hostEnvironment)
{
    var strictFromConfig = FrontendConfiguration.IsStrictConfiguration(configuration);
    return strictFromConfig || !hostEnvironment.IsDevelopment();
}

static void EnsureScope(ICollection<string> scopes, string scope)
{
    if (scopes.Any(existing => string.Equals(existing, scope, StringComparison.OrdinalIgnoreCase)))
        return;

    scopes.Add(scope);
}
