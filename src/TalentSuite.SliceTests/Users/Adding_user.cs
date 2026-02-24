using System.Net;
using System.Net.Http.Json;
using TalentSuite.Shared.Messaging;
using TalentSuite.Shared.Messaging.Commands;
using TalentSuite.Shared.Users;
using TalentSuite.SliceTests.Infrastructure;

namespace TalentSuite.SliceTests.Users;

public class Adding_user : SliceTestBase
{
    [Test]
    public async Task Post_Users_Creates_User_And_Publishes_InviteUser_Command()
    {
        var createRequest = new CreateUserRequest
        {
            Name = "Slice Test User",
            Email = "slice.user@talentconsulting.local",
            Role = UserRole.User,
            HasAcceptedRegistration = false
        };

        var createResponse = await Client.PostAsJsonAsync("/api/users", createRequest);

        Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var createdUser = await createResponse.Content.ReadFromJsonAsync<UserResponse>();
        Assert.That(createdUser, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(createdUser!.Id, Is.Not.Empty);
            Assert.That(createdUser.Email, Is.EqualTo(createRequest.Email));
            Assert.That(createdUser.InvitationToken, Is.Not.Empty);
        });

        var getResponse = await Client.GetAsync($"/api/users/{Uri.EscapeDataString(createdUser!.Id)}");
        Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var bus = GetRequiredService<IAzureServiceBusClient>() as InMemoryAzureServiceBusClient;
        Assert.That(bus, Is.Not.Null);
        Assert.That(bus!.Messages, Has.Count.EqualTo(1));

        var published = bus.Messages.Single();
        Assert.That(published.EntityName, Is.EqualTo("invite-user"));
        Assert.That(published.Payload, Is.TypeOf<InviteUserCommand>());

        var command = (InviteUserCommand)published.Payload;
        Assert.Multiple(() =>
        {
            Assert.That(command.UserId, Is.EqualTo(createdUser.Id));
            Assert.That(command.Email, Is.EqualTo(createdUser.Email));
            Assert.That(command.InvitationToken, Is.EqualTo(createdUser.InvitationToken));
        });
    }
}
