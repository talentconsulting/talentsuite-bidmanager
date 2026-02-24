using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TalentSuite.Shared.Bids;
using TalentSuite.Shared.Users;
using TalentSuite.SliceTests.Infrastructure;

namespace TalentSuite.SliceTests.Bids;

public class Authroisation_for_user_role
{
    [Test]
    public async Task AssignedUserRole_User_CanAccessBidAndDraftWork_ButCannotCreateBidOrManageUsers()
    {
        using var factory = new AuthenticatedTestWebApplicationFactory();
        using var client = factory.CreateClient();

        SetIdentityHeaders(client, subject: "admin-seed", username: "admin-seed", roles: "admin,user");
        var (bidId, questionId) = await CreateBidWithOneQuestionAsync(client);

        var createUserResponse = await client.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            Name = "Assigned User",
            Email = "assigned.user@talentconsulting.local",
            Role = UserRole.User,
            HasAcceptedRegistration = false
        });
        Assert.That(createUserResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var assignedUser = await createUserResponse.Content.ReadFromJsonAsync<UserResponse>();
        Assert.That(assignedUser, Is.Not.Null);

        var assignResponse = await client.PostAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/users",
            new UserAssignmentRequest { UserId = assignedUser!.Id });
        Assert.That(assignResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        SetIdentityHeaders(client, subject: assignedUser.Id, username: "assigned.user", roles: "user"); // user-only role

        var viewBidResponse = await client.GetAsync($"/api/bids/{Uri.EscapeDataString(bidId)}");
        Assert.That(viewBidResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var addDraftResponse = await client.PostAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/drafts",
            new DraftRequest
            {
                Response = "User-role draft content."
            });
        Assert.That(addDraftResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var createdDraft = await addDraftResponse.Content.ReadFromJsonAsync<CreateAssetResponse>();
        Assert.That(createdDraft, Is.Not.Null);
        Assert.That(createdDraft!.Id, Is.Not.Empty);

        var addDraftCommentResponse = await client.PostAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/drafts/{Uri.EscapeDataString(createdDraft.Id)}/comments",
            new AddDraftCommentRequest
            {
                Comment = "Assigned user can comment on their bid draft.",
                UserId = assignedUser.Id,
                AuthorName = assignedUser.Name
            });
        Assert.That(addDraftCommentResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var addedComment = await addDraftCommentResponse.Content.ReadFromJsonAsync<DraftCommentResponse>();
        Assert.That(addedComment, Is.Not.Null);
        Assert.That(addedComment!.Comment, Is.EqualTo("Assigned user can comment on their bid draft."));

        var createBidAsUserResponse = await client.PostAsJsonAsync("/api/bids", new CreateBidRequest
        {
            Company = "Should fail",
            Summary = "User-only cannot create bids",
            Questions =
            [
                new CreateQuestionRequest
                {
                    Category = "General",
                    Number = "1",
                    Title = "No access",
                    Description = "No access",
                    Length = "100 words",
                    Weighting = 10,
                    Required = true,
                    NiceToHave = false
                }
            ]
        });
        Assert.That(createBidAsUserResponse.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

        var manageBidUsersAsUserResponse = await client.PostAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/users",
            new UserAssignmentRequest { UserId = assignedUser.Id });
        Assert.That(manageBidUsersAsUserResponse.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
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

    private static async Task<(string BidId, string QuestionId)> CreateBidWithOneQuestionAsync(HttpClient client)
    {
        var createResponse = await client.PostAsJsonAsync("/api/bids", new CreateBidRequest
        {
            Company = "Slice Access Co",
            Summary = "Assigned user access test bid",
            Questions =
            [
                new CreateQuestionRequest
                {
                    Category = "General",
                    Number = "1",
                    Title = "Can assigned users comment?",
                    Description = "Access check.",
                    Length = "200 words",
                    Weighting = 10,
                    Required = true,
                    NiceToHave = false
                }
            ]
        });
        Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var createJson = await createResponse.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createJson);
        var bidId = createDoc.RootElement.GetProperty("result").GetString();
        Assert.That(string.IsNullOrWhiteSpace(bidId), Is.False);

        var bidResponse = await client.GetAsync($"/api/bids/{Uri.EscapeDataString(bidId!)}");
        Assert.That(bidResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var bid = await bidResponse.Content.ReadFromJsonAsync<BidResponse>();
        Assert.That(bid, Is.Not.Null);
        Assert.That(bid!.Questions, Is.Not.Empty);

        var questionId = bid.Questions[0].Id;
        Assert.That(string.IsNullOrWhiteSpace(questionId), Is.False);
        return (bidId!, questionId);
    }
}
