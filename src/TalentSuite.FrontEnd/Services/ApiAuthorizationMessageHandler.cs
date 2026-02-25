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
            ?? configuration["TALENTSERVER_HTTP"];

        if (string.IsNullOrWhiteSpace(apiBaseAddress))
            throw new InvalidOperationException(
                "Missing TALENTSERVER_HTTPS/TALENTSERVER_HTTP configuration for authorized API URL.");

        ConfigureHandler(
            authorizedUrls: [apiBaseAddress]
        );
    }
}
