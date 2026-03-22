namespace TalentSuite.FrontEnd.Configuration;

public static class FrontendConfiguration
{
    public static string ResolveApiBaseAddress(IConfiguration configuration, bool strictConfiguration)
    {
        var apiBaseAddress = configuration["TALENTSERVER_HTTPS"]
                             ?? configuration["TALENTSERVER_HTTP"];

        if (!string.IsNullOrWhiteSpace(apiBaseAddress))
            return apiBaseAddress;

        if (!strictConfiguration)
            return "https://localhost:5001";

        throw new InvalidOperationException(
            "Missing TALENTSERVER_HTTPS/TALENTSERVER_HTTP configuration for frontend API base address.");
    }

    public static string ResolveAuthorizedApiUrl(IConfiguration configuration)
    {
        var strictConfiguration = IsStrictConfiguration(configuration);
        var apiBaseAddress = configuration["TALENTSERVER_HTTPS"]
                             ?? configuration["TALENTSERVER_HTTP"];

        if (!string.IsNullOrWhiteSpace(apiBaseAddress))
            return apiBaseAddress;

        if (!strictConfiguration)
            return "https://localhost:5001";

        throw new InvalidOperationException(
            "Missing TALENTSERVER_HTTPS/TALENTSERVER_HTTP configuration for authorized API URL.");
    }

    public static string ResolveKeycloakAuthority(IConfiguration configuration, bool strictConfiguration)
    {
        var keycloakAuthority = TryResolveConfiguredKeycloakAuthority(configuration);

        if (!string.IsNullOrWhiteSpace(keycloakAuthority))
            return keycloakAuthority;

        if (!strictConfiguration)
            return "http://localhost:80/realms/TalentConsulting";

        throw new InvalidOperationException(
            "Missing KEYCLOAK_AUTHORITY (or KEYCLOAK_HTTPS/KEYCLOAK_HTTP) configuration for OIDC authority.");
    }

    public static string? TryResolveConfiguredKeycloakAuthority(IConfiguration configuration)
        => configuration["KEYCLOAK_AUTHORITY"] ?? BuildKeycloakAuthorityFromEndpointVariables(configuration);

    public static string ResolveKeycloakClientId(IConfiguration configuration, bool strictConfiguration)
    {
        var keycloakClientId = configuration["KEYCLOAK_CLIENT_ID"];
        if (!string.IsNullOrWhiteSpace(keycloakClientId))
            return keycloakClientId;

        if (!strictConfiguration)
            return "talentsuite-frontend";

        throw new InvalidOperationException(
            "Missing KEYCLOAK_CLIENT_ID configuration for OIDC client id.");
    }

    public static bool IsAuthenticationEnabled(IConfiguration configuration, bool strictConfiguration)
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

    public static bool IsStrictConfiguration(IConfiguration configuration)
    {
        var strictFromConfig = configuration["STRICT_CONFIGURATION"];
        return bool.TryParse(strictFromConfig, out var parsed) && parsed;
    }

    public static string? BuildKeycloakAuthorityFromEndpointVariables(IConfiguration configuration)
    {
        var keycloakBaseAddress = configuration["KEYCLOAK_HTTPS"]
                                  ?? configuration["KEYCLOAK_HTTP"];
        if (string.IsNullOrWhiteSpace(keycloakBaseAddress))
            return null;

        return $"{keycloakBaseAddress.TrimEnd('/')}/realms/TalentConsulting";
    }
}
