using System.Net;
using System.Net.Http.Json;
using TalentSuite.Server.Users.Services;
using TalentSuite.Shared.Users;
using TalentSuite.SliceTests.Infrastructure;

namespace TalentSuite.SliceTests.Users;

public class Inviting_user : SliceTestBase
{
    [Test]
    public async Task ResendInvite_ForPendingUser_ReturnsNewInvitationToken()
    {
        var created = await CreateUserAsync("resend.user@talentconsulting.local");
        var originalToken = created.InvitationToken;

        var resendResponse = await Client.PostAsync(
            $"/api/users/{Uri.EscapeDataString(created.Id)}/resend-invite",
            content: null);

        Assert.That(resendResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var resendBody = await resendResponse.Content.ReadFromJsonAsync<ResendInviteResponse>();
        Assert.That(resendBody, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(resendBody!.InvitationToken, Is.Not.Empty);
            Assert.That(resendBody.InvitationToken, Is.Not.EqualTo(originalToken));
            Assert.That(resendBody.InvitationExpiresAtUtc.HasValue, Is.True);
        });
    }

    [Test]
    public async Task RegisterInvite_AcceptsInvite_AndLinksIdentity()
    {
        var created = await CreateUserAsync("register.user@talentconsulting.local");

        var registerResponse = await Client.PostAsJsonAsync("/api/users/accept-invite/register", new RegisterInviteRequest
        {
            InvitationToken = created.InvitationToken,
            Username = "register.user",
            Password = "Passw0rd!"
        });

        Assert.That(registerResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var registered = await registerResponse.Content.ReadFromJsonAsync<UserResponse>();
        Assert.That(registered, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(registered!.Id, Is.EqualTo(created.Id));
            Assert.That(registered.HasAcceptedRegistration, Is.True);
            Assert.That(registered.IdentityProvider, Is.EqualTo("keycloak"));
            Assert.That(registered.IdentitySubject, Is.EqualTo("kc-register.user"));
            Assert.That(registered.IdentityUsername, Is.EqualTo("register.user"));
            Assert.That(registered.InvitationToken, Is.Empty);
        });

        var keycloak = GetRequiredService<IKeycloakAdminService>() as InMemoryKeycloakAdminService;
        Assert.That(keycloak, Is.Not.Null);
        Assert.That(keycloak!.CreatedIdentities, Has.Count.EqualTo(1));
        Assert.That(keycloak.CreatedIdentities.Single().Username, Is.EqualTo("register.user"));
    }

    private async Task<UserResponse> CreateUserAsync(string email)
    {
        var createResponse = await Client.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            Name = "Slice Invite User",
            Email = email,
            Role = UserRole.User,
            HasAcceptedRegistration = false
        });
        Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var created = await createResponse.Content.ReadFromJsonAsync<UserResponse>();
        Assert.That(created, Is.Not.Null);
        return created!;
    }
}
