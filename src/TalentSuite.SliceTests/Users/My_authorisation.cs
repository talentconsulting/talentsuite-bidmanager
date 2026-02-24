using System.Net;
using System.Net.Http.Json;
using TalentSuite.Shared.Users;
using TalentSuite.SliceTests.Infrastructure;

namespace TalentSuite.SliceTests.Users;

public class My_authorisation
{
    [Test]
    public async Task GetCurrentAuthorisation_AdminRole_ReturnsIsAdminTrue()
    {
        using var factory = new AuthenticatedTestWebApplicationFactory();
        using var client = factory.CreateClient();

        SetIdentityHeaders(client, subject: "admin-seed", username: "admin-seed", roles: "admin,user");

        var response = await client.GetAsync("/api/users/me-authorisation");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var payload = await response.Content.ReadFromJsonAsync<MyAuthorisationResponse>();
        Assert.That(payload, Is.Not.Null);
        Assert.That(payload!.IsAdmin, Is.True);
        Assert.That(payload.Roles, Does.Contain("admin"));
    }

    [Test]
    public async Task GetCurrentUserProfile_KnownUser_ReturnsProfile()
    {
        using var factory = new AuthenticatedTestWebApplicationFactory();
        using var client = factory.CreateClient();

        SetIdentityHeaders(client, subject: "admin-seed", username: "admin-seed", roles: "admin,user");

        var createResponse = await client.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            Name = "Slice Profile User",
            Email = "slice.profile.user@talentconsulting.local",
            Role = UserRole.User,
            HasAcceptedRegistration = false
        });
        Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var created = await createResponse.Content.ReadFromJsonAsync<UserResponse>();
        Assert.That(created, Is.Not.Null);
        Assert.That(string.IsNullOrWhiteSpace(created!.Id), Is.False);

        SetIdentityHeaders(client, subject: created.Id, username: "slice-profile-user", roles: "user");

        var profileResponse = await client.GetAsync("/api/users/me");
        Assert.That(profileResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var profile = await profileResponse.Content.ReadFromJsonAsync<CurrentUserProfileResponse>();
        Assert.That(profile, Is.Not.Null);
        Assert.That(profile!.Id, Is.EqualTo(created.Id));
        Assert.That(profile.Name, Is.EqualTo("Slice Profile User"));
        Assert.That(profile.Email, Is.EqualTo("slice.profile.user@talentconsulting.local"));
    }

    [Test]
    public async Task GetCurrentUserProfile_UnknownUser_ReturnsNotFound()
    {
        using var factory = new AuthenticatedTestWebApplicationFactory();
        using var client = factory.CreateClient();

        SetIdentityHeaders(client, subject: "missing-user", username: "missing-user", roles: "user");

        var profileResponse = await client.GetAsync("/api/users/me");
        Assert.That(profileResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private static void SetIdentityHeaders(HttpClient client, string subject, string username, string roles)
    {
        client.DefaultRequestHeaders.Remove(HeaderTestAuthenticationHandler.SubjectHeader);
        client.DefaultRequestHeaders.Remove(HeaderTestAuthenticationHandler.UsernameHeader);
        client.DefaultRequestHeaders.Remove(HeaderTestAuthenticationHandler.RolesHeader);

        client.DefaultRequestHeaders.Add(HeaderTestAuthenticationHandler.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(HeaderTestAuthenticationHandler.UsernameHeader, username);
        client.DefaultRequestHeaders.Add(HeaderTestAuthenticationHandler.RolesHeader, roles);
    }

    private sealed class MyAuthorisationResponse
    {
        public bool IsAdmin { get; set; }
        public List<string> Roles { get; set; } = new();
    }
}
