using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TalentSuite.Server.Configuration;

[ApiController]
[Route("api/runtime/auth")]
[AllowAnonymous]
public sealed class RuntimeAuthController(IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    public ActionResult<RuntimeAuthResponse> Get()
    {
        var keycloakAuthority = Environment.GetEnvironmentVariable("KEYCLOAK_AUTHORITY")
                                ?? BuildKeycloakAuthorityFromEndpointVariables()
                                ?? configuration["KEYCLOAK_AUTHORITY"];

        return Ok(new RuntimeAuthResponse
        {
            KeycloakAuthority = keycloakAuthority
        });
    }

    private static string? BuildKeycloakAuthorityFromEndpointVariables()
    {
        var keycloakBaseAddress = Environment.GetEnvironmentVariable("KEYCLOAK_HTTPS")
                                  ?? Environment.GetEnvironmentVariable("KEYCLOAK_HTTP");
        if (string.IsNullOrWhiteSpace(keycloakBaseAddress))
            return null;

        return $"{NormalizeKeycloakBaseAddress(keycloakBaseAddress)}/realms/TalentConsulting";
    }

    private static string NormalizeKeycloakBaseAddress(string keycloakBaseAddress)
    {
        if (!Uri.TryCreate(keycloakBaseAddress, UriKind.Absolute, out var uri))
            return keycloakBaseAddress.TrimEnd('/');

        var builder = new UriBuilder(uri);
        if (builder.Scheme == Uri.UriSchemeHttp
            && builder.Host.EndsWith(".azurecontainerapps.io", StringComparison.OrdinalIgnoreCase))
        {
            builder.Scheme = Uri.UriSchemeHttps;
            builder.Port = -1;
        }

        return builder.Uri.ToString().TrimEnd('/');
    }

    public sealed class RuntimeAuthResponse
    {
        public string? KeycloakAuthority { get; set; }
    }
}
