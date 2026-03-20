using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication.Internal;

namespace TalentSuite.FrontEnd.Security;

public sealed class KeycloakAccountClaimsPrincipalFactory
    : AccountClaimsPrincipalFactory<RemoteUserAccount>
{
    private readonly IConfiguration _configuration;

    public KeycloakAccountClaimsPrincipalFactory(
        IAccessTokenProviderAccessor accessor,
        IConfiguration configuration)
        : base(accessor)
    {
        _configuration = configuration;
    }

    public override async ValueTask<ClaimsPrincipal> CreateUserAsync(
        RemoteUserAccount account,
        RemoteAuthenticationUserOptions options)
    {
        var user = await base.CreateUserAsync(account, options);

        if (user.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated)
            return user;

        AddRealmRoles(identity, account);
        AddResourceRoles(identity, account);
        return user;
    }

    private static void AddRealmRoles(ClaimsIdentity identity, RemoteUserAccount account)
    {
        if (!account.AdditionalProperties.TryGetValue("realm_access", out var realmAccess))
            return;

        if (realmAccess is not JsonElement realmElement ||
            !realmElement.TryGetProperty("roles", out var roles) ||
            roles.ValueKind != JsonValueKind.Array)
            return;

        foreach (var role in roles.EnumerateArray())
        {
            AddRole(identity, role.GetString());
        }
    }

    private void AddResourceRoles(ClaimsIdentity identity, RemoteUserAccount account)
    {
        if (!account.AdditionalProperties.TryGetValue("resource_access", out var resourceAccess))
            return;

        if (resourceAccess is not JsonElement resourceElement)
            return;

        var clientId = _configuration["KEYCLOAK_CLIENT_ID"]
            ?? throw new InvalidOperationException("Missing KEYCLOAK_CLIENT_ID configuration.");

        if (!resourceElement.TryGetProperty(clientId, out var clientNode))
            return;

        if (!clientNode.TryGetProperty("roles", out var roles) || roles.ValueKind != JsonValueKind.Array)
            return;

        foreach (var role in roles.EnumerateArray())
        {
            AddRole(identity, role.GetString());
        }
    }

    private static void AddRole(ClaimsIdentity identity, string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return;

        if (!identity.HasClaim(identity.RoleClaimType, role))
            identity.AddClaim(new Claim(identity.RoleClaimType, role));
    }
}
