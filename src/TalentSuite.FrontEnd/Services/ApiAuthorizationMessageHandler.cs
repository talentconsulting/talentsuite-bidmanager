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
        {
            var strictFromConfig = configuration["STRICT_CONFIGURATION"];
            var strictConfiguration = bool.TryParse(strictFromConfig, out var parsed)
                ? parsed
                : false;

            if (!strictConfiguration)
            {
                apiBaseAddress = "https://localhost:5001";
            }
            else
            {
                throw new InvalidOperationException(
                    "Missing TALENTSERVER_HTTPS/TALENTSERVER_HTTP configuration for authorized API URL.");
            }
        }

        ConfigureHandler(
            authorizedUrls: [apiBaseAddress]
        );
    }
}
