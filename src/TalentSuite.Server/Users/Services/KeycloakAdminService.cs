using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace TalentSuite.Server.Users.Services;

public sealed class KeycloakAdminService : IKeycloakAdminService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<KeycloakAdminService> _logger;
    private readonly string? _keycloakBaseUrl;
    private readonly string _targetRealm;
    private readonly string _adminRealm;
    private readonly string _adminUsername;
    private readonly string _adminPassword;
    private readonly string _adminClientId;
    private readonly string? _adminClientSecret;

    public KeycloakAdminService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<KeycloakAdminService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        var configuredAuthority = Environment.GetEnvironmentVariable("KEYCLOAK_AUTHORITY")
                                  ?? configuration["KEYCLOAK_AUTHORITY"];
        var configuredBase = Environment.GetEnvironmentVariable("KEYCLOAK_BASE_URL")
                             ?? configuration["KEYCLOAK_BASE_URL"]
                             ?? BuildKeycloakBaseUrlFromEndpointVariables();
        _keycloakBaseUrl = configuredBase ?? TryBuildBaseUrlFromAuthority(configuredAuthority);
        _targetRealm = Environment.GetEnvironmentVariable("KEYCLOAK_REALM")
                 ?? configuration["KEYCLOAK_REALM"]
                 ?? "TalentConsulting";
        _adminRealm = Environment.GetEnvironmentVariable("KEYCLOAK_ADMIN_REALM")
                      ?? configuration["KEYCLOAK_ADMIN_REALM"]
                      ?? "master";
        _adminUsername = Environment.GetEnvironmentVariable("KEYCLOAK_ADMIN_USERNAME")
                         ?? configuration["KEYCLOAK_ADMIN_USERNAME"]
                         ?? "admin";
        _adminPassword = Environment.GetEnvironmentVariable("KEYCLOAK_ADMIN_PASSWORD")
                         ?? configuration["KEYCLOAK_ADMIN_PASSWORD"]
                         ?? string.Empty;
        _adminClientId = Environment.GetEnvironmentVariable("KEYCLOAK_ADMIN_CLIENT_ID")
                         ?? configuration["KEYCLOAK_ADMIN_CLIENT_ID"]
                         ?? "admin-cli";
        _adminClientSecret = Environment.GetEnvironmentVariable("KEYCLOAK_ADMIN_CLIENT_SECRET")
                             ?? configuration["KEYCLOAK_ADMIN_CLIENT_SECRET"];
    }

    public async Task<bool> DeleteUserAsync(string? userId, string? username, string? email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId)
            && string.IsNullOrWhiteSpace(username)
            && string.IsNullOrWhiteSpace(email))
            return false;

        if (string.IsNullOrWhiteSpace(_keycloakBaseUrl))
        {
            _logger.LogWarning("Keycloak base URL is not set. Cannot delete user in Keycloak.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(_adminPassword))
        {
            _logger.LogWarning("KEYCLOAK_ADMIN_PASSWORD is not set. Cannot delete user in Keycloak.");
            return false;
        }

        var http = _httpClientFactory.CreateClient();

        var tokenResponse = await RequestAdminTokenAsync(http, ct);
        if (tokenResponse is null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
            return false;

        var candidateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(userId))
            candidateIds.Add(userId);

        if (!string.IsNullOrWhiteSpace(username))
        {
            var idsByUsername = await FindUserIdsByUsernameAsync(http, tokenResponse.AccessToken, username, ct);
            foreach (var id in idsByUsername)
                candidateIds.Add(id);
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            var idsByEmail = await FindUserIdsByEmailAsync(http, tokenResponse.AccessToken, email, ct);
            foreach (var id in idsByEmail)
                candidateIds.Add(id);
        }

        if (candidateIds.Count == 0)
        {
            _logger.LogWarning(
                "No Keycloak user candidates found for delete. Subject={Subject}, Username={Username}, Email={Email}.",
                userId,
                username,
                email);
            return false;
        }

        var allSucceeded = true;
        foreach (var candidateId in candidateIds)
        {
            var deleted = await DeleteByIdAsync(http, tokenResponse.AccessToken, candidateId, ct);
            allSucceeded &= deleted;
        }

        return allSucceeded;
    }

    public async Task<string?> CreateUserAsync(string username, string email, string? name, string password, string role, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return null;

        if (string.IsNullOrWhiteSpace(_keycloakBaseUrl) || string.IsNullOrWhiteSpace(_adminPassword))
            return null;

        _logger.LogInformation(
            "Keycloak CreateUserAsync starting. Username={Username}, Email={Email}, Role={Role}, Realm={Realm}, BaseUrl={BaseUrl}.",
            username,
            email,
            role,
            _targetRealm,
            _keycloakBaseUrl);

        var http = _httpClientFactory.CreateClient();
        var tokenResponse = await RequestAdminTokenAsync(http, ct);
        if (tokenResponse is null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
            return null;

        var userPayload = new
        {
            username,
            email = string.IsNullOrWhiteSpace(email) ? null : email,
            enabled = true,
            emailVerified = true,
            firstName = name,
            credentials = new[]
            {
                new
                {
                    type = "password",
                    value = password,
                    temporary = false
                }
            }
        };

        using var createRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_keycloakBaseUrl.TrimEnd('/')}/admin/realms/{Uri.EscapeDataString(_targetRealm)}/users")
        {
            Content = JsonContent.Create(userPayload)
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResponse.AccessToken);

        var createResponse = await http.SendAsync(createRequest, ct);

        string? createdUserId = null;
        if (createResponse.IsSuccessStatusCode)
        {
            createdUserId = TryParseUserIdFromLocation(createResponse.Headers.Location);
        }
        else if (createResponse.StatusCode != System.Net.HttpStatusCode.Conflict)
        {
            var body = await createResponse.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "Failed to create Keycloak user {Username}. Status: {StatusCode}. Body: {Body}",
                username,
                (int)createResponse.StatusCode,
                body);
            return null;
        }

        if (string.IsNullOrWhiteSpace(createdUserId))
        {
            var candidates = await FindUserIdsByUsernameAsync(http, tokenResponse.AccessToken, username, ct);
            createdUserId = candidates.FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(createdUserId))
            return null;

        var roleRepresentation = await GetRealmRoleAsync(http, tokenResponse.AccessToken, role, ct);
        if (roleRepresentation is null)
        {
            _logger.LogWarning(
                "Keycloak CreateUserAsync could not resolve realm role {Role} for username {Username}.",
                role,
                username);
            return null;
        }

        var roleAssigned = await AssignRealmRoleAsync(http, tokenResponse.AccessToken, createdUserId, roleRepresentation, ct);
        if (!roleAssigned)
        {
            _logger.LogWarning(
                "Keycloak CreateUserAsync failed assigning realm role {Role} to user {UserId} ({Username}).",
                roleRepresentation.Name,
                createdUserId,
                username);
            return null;
        }

        _logger.LogInformation(
            "Keycloak CreateUserAsync assigned realm role {Role} to user {UserId} ({Username}).",
            roleRepresentation.Name,
            createdUserId,
            username);

        return createdUserId;
    }

    public async Task<bool> SyncRealmRoleAsync(string? userId, string? username, string? email, string role, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId)
            && string.IsNullOrWhiteSpace(username)
            && string.IsNullOrWhiteSpace(email))
            return false;

        if (string.IsNullOrWhiteSpace(role))
            return false;

        if (string.IsNullOrWhiteSpace(_keycloakBaseUrl) || string.IsNullOrWhiteSpace(_adminPassword))
            return false;

        var http = _httpClientFactory.CreateClient();
        var tokenResponse = await RequestAdminTokenAsync(http, ct);
        if (tokenResponse is null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
            return false;

        var candidateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(userId))
            candidateIds.Add(userId);

        if (!string.IsNullOrWhiteSpace(username))
        {
            var idsByUsername = await FindUserIdsByUsernameAsync(http, tokenResponse.AccessToken, username, ct);
            foreach (var id in idsByUsername)
                candidateIds.Add(id);
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            var idsByEmail = await FindUserIdsByEmailAsync(http, tokenResponse.AccessToken, email, ct);
            foreach (var id in idsByEmail)
                candidateIds.Add(id);
        }

        if (candidateIds.Count == 0)
            return false;

        var desiredRole = await GetRealmRoleAsync(http, tokenResponse.AccessToken, role, ct);
        if (desiredRole is null)
            return false;

        var allSucceeded = true;
        foreach (var candidateId in candidateIds)
        {
            var existingRoles = await GetUserRealmRolesAsync(http, tokenResponse.AccessToken, candidateId, ct);
            var managedRoles = existingRoles
                .Where(r => string.Equals(r.Name, "admin", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(r.Name, "user", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var rolesToRemove = managedRoles
                .Where(r => !string.Equals(r.Name, desiredRole.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (rolesToRemove.Count > 0)
            {
                var removed = await RemoveRealmRolesAsync(http, tokenResponse.AccessToken, candidateId, rolesToRemove, ct);
                allSucceeded &= removed;
            }

            var alreadyAssigned = managedRoles.Any(r =>
                string.Equals(r.Name, desiredRole.Name, StringComparison.OrdinalIgnoreCase));
            if (!alreadyAssigned)
            {
                var assigned = await AssignRealmRoleAsync(http, tokenResponse.AccessToken, candidateId, desiredRole, ct);
                allSucceeded &= assigned;
            }
        }

        return allSucceeded;
    }

    private async Task<KeycloakTokenResponse?> RequestAdminTokenAsync(HttpClient http, CancellationToken ct)
    {
        var baseUrl = _keycloakBaseUrl!.TrimEnd('/');
        var urls = new[]
        {
            $"{baseUrl}/realms/{Uri.EscapeDataString(_adminRealm)}/protocol/openid-connect/token",
            $"{baseUrl}/auth/realms/{Uri.EscapeDataString(_adminRealm)}/protocol/openid-connect/token"
        }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = _adminClientId,
            ["username"] = _adminUsername,
            ["password"] = _adminPassword
        };
        if (!string.IsNullOrWhiteSpace(_adminClientSecret))
            form["client_secret"] = _adminClientSecret;

        for (var index = 0; index < urls.Length; index++)
        {
            using var content = new FormUrlEncodedContent(form);
            var url = urls[index];
            var response = await http.PostAsync(url, content, ct);
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadFromJsonAsync<KeycloakTokenResponse>(ct);

            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "Failed to get Keycloak admin token from {Url}. Status: {StatusCode}. Body: {Body}",
                url,
                (int)response.StatusCode,
                body);

            var shouldTryNextUrl = index < urls.Length - 1
                                   && (response.StatusCode == System.Net.HttpStatusCode.NotFound
                                       || response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed);
            if (!shouldTryNextUrl)
                return null;
        }

        return null;
    }

    private static string? BuildKeycloakBaseUrlFromEndpointVariables()
    {
        var keycloakBaseAddress = Environment.GetEnvironmentVariable("KEYCLOAK_HTTPS")
                                  ?? Environment.GetEnvironmentVariable("KEYCLOAK_HTTP");
        if (string.IsNullOrWhiteSpace(keycloakBaseAddress))
            return null;

        return NormalizeKeycloakBaseUrl(keycloakBaseAddress);
    }

    private static string? TryBuildBaseUrlFromAuthority(string? authority)
    {
        if (string.IsNullOrWhiteSpace(authority))
            return null;

        if (!Uri.TryCreate(authority, UriKind.Absolute, out var uri))
            return null;

        var path = uri.AbsolutePath.TrimEnd('/');
        const string marker = "/realms/";
        var index = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        var basePath = index >= 0 ? path[..index] : path;

        var builder = new UriBuilder(uri.Scheme, uri.Host, uri.Port, basePath);
        if (builder.Scheme == Uri.UriSchemeHttp
            && builder.Host.EndsWith(".azurecontainerapps.io", StringComparison.OrdinalIgnoreCase))
        {
            builder.Scheme = Uri.UriSchemeHttps;
            builder.Port = -1;
        }
        return builder.Uri.ToString().TrimEnd('/');
    }

    private static string NormalizeKeycloakBaseUrl(string keycloakBaseAddress)
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

    private async Task<bool> DeleteByIdAsync(HttpClient http, string accessToken, string userId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"{_keycloakBaseUrl!.TrimEnd('/')}/admin/realms/{Uri.EscapeDataString(_targetRealm)}/users/{Uri.EscapeDataString(userId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return true;

        var body = await response.Content.ReadAsStringAsync(ct);
        _logger.LogWarning(
            "Failed to delete Keycloak user {UserId}. Status: {StatusCode}. Body: {Body}",
            userId,
            (int)response.StatusCode,
            body);
        return false;
    }

    private async Task<List<string>> FindUserIdsByUsernameAsync(HttpClient http, string accessToken, string username, CancellationToken ct)
    {
        var url =
            $"{_keycloakBaseUrl!.TrimEnd('/')}/admin/realms/{Uri.EscapeDataString(_targetRealm)}/users?username={Uri.EscapeDataString(username)}&exact=true";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return new List<string>();

        var users = await response.Content.ReadFromJsonAsync<List<KeycloakUserSummary>>(ct) ?? new List<KeycloakUserSummary>();
        return users
            .Where(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase))
            .Select(u => u.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();
    }

    private async Task<List<string>> FindUserIdsByEmailAsync(HttpClient http, string accessToken, string email, CancellationToken ct)
    {
        var url =
            $"{_keycloakBaseUrl!.TrimEnd('/')}/admin/realms/{Uri.EscapeDataString(_targetRealm)}/users?email={Uri.EscapeDataString(email)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return new List<string>();

        var users = await response.Content.ReadFromJsonAsync<List<KeycloakUserSummary>>(ct) ?? new List<KeycloakUserSummary>();
        return users
            .Where(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase))
            .Select(u => u.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();
    }

    private async Task<KeycloakRoleRepresentation?> GetRealmRoleAsync(
        HttpClient http,
        string accessToken,
        string roleName,
        CancellationToken ct)
    {
        var url =
            $"{_keycloakBaseUrl!.TrimEnd('/')}/admin/realms/{Uri.EscapeDataString(_targetRealm)}/roles/{Uri.EscapeDataString(roleName)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "Failed to get Keycloak role {Role}. Status: {StatusCode}. Body: {Body}",
                roleName,
                (int)response.StatusCode,
                body);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<KeycloakRoleRepresentation>(ct);
    }

    private async Task<List<KeycloakRoleRepresentation>> GetUserRealmRolesAsync(
        HttpClient http,
        string accessToken,
        string userId,
        CancellationToken ct)
    {
        var url =
            $"{_keycloakBaseUrl!.TrimEnd('/')}/admin/realms/{Uri.EscapeDataString(_targetRealm)}/users/{Uri.EscapeDataString(userId)}/role-mappings/realm";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return new List<KeycloakRoleRepresentation>();

        return await response.Content.ReadFromJsonAsync<List<KeycloakRoleRepresentation>>(ct)
               ?? new List<KeycloakRoleRepresentation>();
    }

    private async Task<bool> AssignRealmRoleAsync(
        HttpClient http,
        string accessToken,
        string userId,
        KeycloakRoleRepresentation role,
        CancellationToken ct)
    {
        var url =
            $"{_keycloakBaseUrl!.TrimEnd('/')}/admin/realms/{Uri.EscapeDataString(_targetRealm)}/users/{Uri.EscapeDataString(userId)}/role-mappings/realm";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new[] { role })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NoContent)
            return true;

        var body = await response.Content.ReadAsStringAsync(ct);
        _logger.LogWarning(
            "Failed to assign Keycloak role {Role} to user {UserId}. Status: {StatusCode}. Body: {Body}",
            role.Name,
            userId,
            (int)response.StatusCode,
            body);
        return false;
    }

    private async Task<bool> RemoveRealmRolesAsync(
        HttpClient http,
        string accessToken,
        string userId,
        IReadOnlyCollection<KeycloakRoleRepresentation> roles,
        CancellationToken ct)
    {
        if (roles.Count == 0)
            return true;

        var url =
            $"{_keycloakBaseUrl!.TrimEnd('/')}/admin/realms/{Uri.EscapeDataString(_targetRealm)}/users/{Uri.EscapeDataString(userId)}/role-mappings/realm";
        using var request = new HttpRequestMessage(HttpMethod.Delete, url)
        {
            Content = JsonContent.Create(roles)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NoContent)
            return true;

        var body = await response.Content.ReadAsStringAsync(ct);
        _logger.LogWarning(
            "Failed to remove Keycloak realm roles from user {UserId}. Status: {StatusCode}. Body: {Body}",
            userId,
            (int)response.StatusCode,
            body);
        return false;
    }

    private static string? TryParseUserIdFromLocation(Uri? location)
    {
        if (location is null)
            return null;

        var segments = location.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 0 ? null : segments[^1];
    }

    private sealed class KeycloakTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;
    }

    private sealed class KeycloakUserSummary
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;
    }

    private sealed class KeycloakRoleRepresentation
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }
}
