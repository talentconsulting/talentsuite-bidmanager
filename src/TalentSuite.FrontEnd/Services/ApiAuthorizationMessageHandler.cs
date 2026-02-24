using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace TalentSuite.FrontEnd.Services;

public sealed class ApiAuthorizationMessageHandler : AuthorizationMessageHandler
{
    public ApiAuthorizationMessageHandler(
        IAccessTokenProvider provider,
        NavigationManager navigationManager,
        IConfiguration configuration)
        : base(provider, navigationManager)
    {
        var apiBaseAddress = configuration["TALENTSERVER_HTTPS"]
            ?? configuration["TALENTSERVER_HTTP"]
            ?? "https://localhost:5001";

        ConfigureHandler(
            authorizedUrls: [apiBaseAddress]
        );
    }
}
