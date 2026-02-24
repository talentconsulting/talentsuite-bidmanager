using System.Net.Http.Json;
using TalentSuite.Shared.Users;

namespace TalentSuite.FrontEnd.Pages;

public sealed class AcceptInviteApiClient(HttpClient http)
{
    public async Task AcceptInviteAsync(string invitationToken, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("api/users/accept-invite", new AcceptInviteRequest
        {
            InvitationToken = invitationToken
        }, ct);

        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(ct);
        var message = string.IsNullOrWhiteSpace(body)
            ? $"Failed to accept invite: {(int)response.StatusCode} {response.ReasonPhrase}"
            : body;

        throw new InvalidOperationException(message);
    }

    public async Task RegisterInviteAsync(string invitationToken, string username, string password, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("api/users/accept-invite/register", new RegisterInviteRequest
        {
            InvitationToken = invitationToken,
            Username = username,
            Password = password
        }, ct);

        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(ct);
        var message = string.IsNullOrWhiteSpace(body)
            ? $"Failed to register invite: {(int)response.StatusCode} {response.ReasonPhrase}"
            : body;

        throw new InvalidOperationException(message);
    }
}
