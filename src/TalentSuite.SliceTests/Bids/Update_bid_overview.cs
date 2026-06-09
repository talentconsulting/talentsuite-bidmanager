using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TalentSuite.Shared.Bids;
using TalentSuite.Shared.Users;
using TalentSuite.SliceTests.Infrastructure;

namespace TalentSuite.SliceTests.Bids;

public class Update_bid_overview
{
    [Test]
    public async Task UpdateOverview_Admin_PersistsAcrossGet()
    {
        using var factory = new AuthenticatedTestWebApplicationFactory();
        using var client = factory.CreateClient();

        SetIdentityHeaders(client, subject: "admin-seed", username: "admin-seed", roles: "admin,user");
        var bidId = await CreateBidAsync(client);

        var updateResponse = await client.PatchAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/overview",
            new UpdateBidOverviewRequest
            {
                UniqueReference = "REF-001",
                Summary = "Updated summary",
                KeyInformation = "Updated key information",
                Budget = "£250,000",
                DeadlineForQualifying = "2026-07-01",
                DeadlineForSubmission = "2026-07-15",
                LengthOfContract = "24 months"
            });

        Assert.That(updateResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var bidResponse = await client.GetAsync($"/api/bids/{Uri.EscapeDataString(bidId)}");
        Assert.That(bidResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var bid = await bidResponse.Content.ReadFromJsonAsync<BidResponse>();
        Assert.That(bid, Is.Not.Null);
        Assert.That(bid!.UniqueReference, Is.EqualTo("REF-001"));
        Assert.That(bid.Summary, Is.EqualTo("Updated summary"));
        Assert.That(bid.KeyInformation, Is.EqualTo("Updated key information"));
        Assert.That(bid.Budget, Is.EqualTo("£250,000"));
        Assert.That(bid.DeadlineForQualifying, Is.EqualTo("2026-07-01"));
        Assert.That(bid.DeadlineForSubmission, Is.EqualTo("2026-07-15"));
        Assert.That(bid.LengthOfContract, Is.EqualTo("24 months"));
    }

    [Test]
    public async Task UpdateOverview_BidManager_CanUpdateAssignedBid()
    {
        using var factory = new AuthenticatedTestWebApplicationFactory();
        using var client = factory.CreateClient();

        SetIdentityHeaders(client, subject: "admin-seed", username: "admin-seed", roles: "admin,user");
        var bidId = await CreateBidAsync(client);

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

        var assignResponse = await client.PostAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/users",
            new UserAssignmentRequest { UserId = bidManager!.Id });
        Assert.That(assignResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        SetIdentityHeaders(client, subject: bidManager.Id, username: "assigned.bid.manager", roles: "bidManager,user");

        var updateResponse = await client.PatchAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/overview",
            new UpdateBidOverviewRequest
            {
                Summary = "Bid manager updated summary",
                Budget = "£300,000"
            });

        Assert.That(updateResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var bidResponse = await client.GetAsync($"/api/bids/{Uri.EscapeDataString(bidId)}");
        Assert.That(bidResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var bid = await bidResponse.Content.ReadFromJsonAsync<BidResponse>();
        Assert.That(bid, Is.Not.Null);
        Assert.That(bid!.Summary, Is.EqualTo("Bid manager updated summary"));
        Assert.That(bid.Budget, Is.EqualTo("£300,000"));
    }

    [Test]
    public async Task UpdateOverview_AssignedUser_IsForbidden()
    {
        using var factory = new AuthenticatedTestWebApplicationFactory();
        using var client = factory.CreateClient();

        SetIdentityHeaders(client, subject: "admin-seed", username: "admin-seed", roles: "admin,user");
        var bidId = await CreateBidAsync(client);

        var userResponse = await client.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            Name = "Assigned User",
            Email = "assigned.user@talentconsulting.local",
            Role = UserRole.User,
            HasAcceptedRegistration = false
        });
        Assert.That(userResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var user = await userResponse.Content.ReadFromJsonAsync<UserResponse>();
        Assert.That(user, Is.Not.Null);

        var assignResponse = await client.PostAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/users",
            new UserAssignmentRequest { UserId = user!.Id });
        Assert.That(assignResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        SetIdentityHeaders(client, subject: user.Id, username: "assigned.user", roles: "user");

        var updateResponse = await client.PatchAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/overview",
            new UpdateBidOverviewRequest
            {
                Summary = "Should not be allowed"
            });

        Assert.That(updateResponse.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
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

    private static async Task<string> CreateBidAsync(HttpClient client)
    {
        var createResponse = await client.PostAsJsonAsync("/api/bids", new CreateBidRequest
        {
            Company = "Slice Overview Co",
            Summary = "Overview update test bid",
            Questions =
            [
                new CreateQuestionRequest
                {
                    Category = "General",
                    Number = "1",
                    Title = "Overview question",
                    Description = "Overview question description",
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

        return bidId!;
    }
}
