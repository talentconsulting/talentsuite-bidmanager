using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TalentSuite.Shared.Bids;
using TalentSuite.Shared.Bids.Ai;
using TalentSuite.Shared.Bids.List;
using TalentSuite.Shared.Users;
using TalentSuite.SliceTests.Infrastructure;

namespace TalentSuite.SliceTests.Bids;

public class Authorisation_for_user_role
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

    [Test]
    public async Task NonAdminUser_NotAssignedToBid_IsForbiddenFromGettingBid()
    {
        using var factory = new AuthenticatedTestWebApplicationFactory();
        using var client = factory.CreateClient();

        SetIdentityHeaders(client, subject: "admin-seed", username: "admin-seed", roles: "admin,user");
        var (bidId, _) = await CreateBidWithOneQuestionAsync(client);

        var createUserResponse = await client.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            Name = "Unassigned User",
            Email = "unassigned.user@talentconsulting.local",
            Role = UserRole.User,
            HasAcceptedRegistration = false
        });
        Assert.That(createUserResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var unassignedUser = await createUserResponse.Content.ReadFromJsonAsync<UserResponse>();
        Assert.That(unassignedUser, Is.Not.Null);

        SetIdentityHeaders(client, subject: unassignedUser!.Id, username: "unassigned.user", roles: "user");

        var response = await client.GetAsync($"/api/bids/{Uri.EscapeDataString(bidId)}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task AssignedUserRole_BidManager_CanManageAssignedBid_ButCannotPerformGlobalAdminActions()
    {
        using var factory = new AuthenticatedTestWebApplicationFactory();
        using var client = factory.CreateClient();

        SetIdentityHeaders(client, subject: "admin-seed", username: "admin-seed", roles: "admin,user");
        var (bidId, questionId) = await CreateBidWithOneQuestionAsync(client);

        var bidManagerResponse = await client.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            Name = "Assigned Bid Manager",
            Email = "assigned.bid.manager@talentconsulting.local",
            Role = UserRole.BidManager,
            HasAcceptedRegistration = false
        });
        Assert.That(bidManagerResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var bidManager = await bidManagerResponse.Content.ReadFromJsonAsync<UserResponse>();
        Assert.That(bidManager, Is.Not.Null);

        var collaboratorResponse = await client.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            Name = "Collaborator User",
            Email = "collaborator.user@talentconsulting.local",
            Role = UserRole.User,
            HasAcceptedRegistration = false
        });
        Assert.That(collaboratorResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var collaborator = await collaboratorResponse.Content.ReadFromJsonAsync<UserResponse>();
        Assert.That(collaborator, Is.Not.Null);

        var assignBidManagerResponse = await client.PostAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/users",
            new UserAssignmentRequest { UserId = bidManager!.Id });
        Assert.That(assignBidManagerResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        SetIdentityHeaders(client, subject: bidManager.Id, username: "assigned.bid.manager", roles: "bidManager,user");

        var listAssignedBidsResponse = await client.GetAsync("/api/bids?page=1&pageSize=10");
        Assert.That(listAssignedBidsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var assignedBids = await listAssignedBidsResponse.Content.ReadFromJsonAsync<PagedBidListResponse>();
        Assert.That(assignedBids, Is.Not.Null);
        Assert.That(assignedBids!.Items.Any(x => string.Equals(x.Id, bidId, StringComparison.OrdinalIgnoreCase)), Is.True);

        var addBidUserResponse = await client.PostAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/users",
            new UserAssignmentRequest { UserId = collaborator!.Id });
        Assert.That(addBidUserResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var addQuestionUserResponse = await client.PostAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/users",
            new QuestionUserAssignmentRequest
            {
                UserId = collaborator.Id,
                Role = QuestionUserRole.Owner
            });
        Assert.That(addQuestionUserResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var askChatResponse = await client.PostAsJsonAsync(
            $"/api/ai/questions/{Uri.EscapeDataString(questionId)}",
            new ChatQuestionRequest
            {
                BidId = bidId,
                QuestionId = questionId,
                FreeTextQuestion = "Summarise the bid intent."
            });
        Assert.That(askChatResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var createBidAsBidManagerResponse = await client.PostAsJsonAsync("/api/bids", new CreateBidRequest
        {
            Company = "Should fail",
            Summary = "Bid manager cannot create bids",
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
        Assert.That(createBidAsBidManagerResponse.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

        var createUserAsBidManagerResponse = await client.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            Name = "Should fail",
            Email = "should.fail@talentconsulting.local",
            Role = UserRole.User,
            HasAcceptedRegistration = false
        });
        Assert.That(createUserAsBidManagerResponse.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
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
