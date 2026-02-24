using System.Net;
using System.Net.Http.Json;
using TalentSuite.Shared.Users;

namespace TalentSuite.FrontEnd.Pages;

public sealed class ProfileApiClient(HttpClient http)
{
    public sealed class AuthorisationInfo
    {
        public bool IsAdmin { get; set; }
        public List<string> Roles { get; set; } = [];
    }

    public async Task<CurrentUserProfileResponse?> GetMyProfileAsync(CancellationToken ct = default)
    {
        var response = await http.GetAsync("api/users/me", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Failed to load profile: {(int)response.StatusCode} {response.ReasonPhrase}");

        return await response.Content.ReadFromJsonAsync<CurrentUserProfileResponse>(ct);
    }

    public async Task<(HttpStatusCode StatusCode, string Body)> GetMyIdentityDebugAsync(CancellationToken ct = default)
    {
        var response = await http.GetAsync("api/users/me-identity-debug", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return (response.StatusCode, body);
    }

    public async Task<AuthorisationInfo?> GetMyAuthorisationAsync(CancellationToken ct = default)
    {
        var response = await http.GetAsync("api/users/me-authorisation", ct);
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<AuthorisationInfo>(ct);
    }
}
