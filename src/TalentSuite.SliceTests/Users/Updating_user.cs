using System.Net;
using System.Net.Http.Json;
using TalentSuite.Shared.Users;
using TalentSuite.SliceTests.Infrastructure;

namespace TalentSuite.SliceTests.Users;

public class Updating_user : SliceTestBase
{
    [Test]
    public async Task Put_Users_WhenUserDoesNotExist_Returns_NotFound()
    {
        var missingUserId = Guid.NewGuid().ToString();
        var updateRequest = new UpdateUserRequest
        {
            Name = "Missing User",
            Email = "missing.user@talentconsulting.local",
            Role = UserRole.User,
            HasAcceptedRegistration = false
        };

        var updateResponse = await Client.PutAsJsonAsync($"/api/users/{Uri.EscapeDataString(missingUserId)}", updateRequest);

        Assert.That(updateResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Put_Users_WhenUserExists_UpdatesUser()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            Name = "Update Target",
            Email = "update.target@talentconsulting.local",
            Role = UserRole.User,
            HasAcceptedRegistration = false
        });
        Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var created = await createResponse.Content.ReadFromJsonAsync<UserResponse>();
        Assert.That(created, Is.Not.Null);
        Assert.That(string.IsNullOrWhiteSpace(created!.Id), Is.False);

        var updateResponse = await Client.PutAsJsonAsync($"/api/users/{Uri.EscapeDataString(created.Id)}", new UpdateUserRequest
        {
            Name = "Updated Name",
            Email = "updated.user@talentconsulting.local",
            Role = UserRole.Admin,
            HasAcceptedRegistration = true
        });
        Assert.That(updateResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var getResponse = await Client.GetAsync($"/api/users/{Uri.EscapeDataString(created.Id)}");
        Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var updated = await getResponse.Content.ReadFromJsonAsync<UserResponse>();
        Assert.That(updated, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(updated!.Name, Is.EqualTo("Updated Name"));
            Assert.That(updated.Email, Is.EqualTo("updated.user@talentconsulting.local"));
            Assert.That(updated.Role, Is.EqualTo(UserRole.Admin));
            Assert.That(updated.HasAcceptedRegistration, Is.True);
        });
    }
}
