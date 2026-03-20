using System.Net;
using System.Net.Http.Json;
using TalentSuite.Shared.Users;
using TalentSuite.SliceTests.Infrastructure;

namespace TalentSuite.SliceTests.Users;

public class Deleting_user : SliceTestBase
{
    [Test]
    public async Task DeleteUser_RemovesCreatedUser()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            Name = "Delete User",
            Email = "delete.user@talentconsulting.local",
            Role = UserRole.User,
            HasAcceptedRegistration = false
        });
        Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var created = await createResponse.Content.ReadFromJsonAsync<UserResponse>();
        Assert.That(created, Is.Not.Null);

        var deleteResponse = await Client.DeleteAsync($"/api/users/{Uri.EscapeDataString(created!.Id)}");
        Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var getResponse = await Client.GetAsync($"/api/users/{Uri.EscapeDataString(created.Id)}");
        Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
