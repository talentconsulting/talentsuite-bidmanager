using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using TalentSuite.FrontEnd.Configuration;

namespace TalentSuite.FrontEnd.Services;

public sealed class ApiAuthorizationMessageHandler : AuthorizationMessageHandler
{
    public ApiAuthorizationMessageHandler(
        IAccessTokenProvider provider,
        NavigationManager navigationManager,
        IConfiguration configuration)
        : base(provider, navigationManager)
    {
        var apiBaseAddress = FrontendConfiguration.ResolveAuthorizedApiUrl(configuration);

        ConfigureHandler(
            authorizedUrls: [apiBaseAddress]
        );
    }
}
