using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TalentSuite.Shared.Bids;
using TalentSuite.Shared.Tasks;
using TalentSuite.SliceTests.Infrastructure;

namespace TalentSuite.SliceTests.Bids;

public class Question_assignments_dashboard
{
    [Test]
    public async Task AddingQuestionAssignment_MakesItVisibleInMyQuestionAssignmentsDashboardEndpoint()
    {
        using var factory = new AuthenticatedTestWebApplicationFactory();
        using var client = factory.CreateClient();

        SetIdentityHeaders(
            client,
            subject: "3e22f0c0-e221-49ba-a85b-483c22002e38",
            username: "richard.parkins",
            roles: "admin,user");

        var (bidId, questionId) = await CreateBidWithOneQuestionAsync(client);

        var addAssignmentResponse = await client.PostAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/users",
            new QuestionUserAssignmentRequest
            {
                UserId = "04d3fde7-8b47-4558-905b-1888fb8a4db0",
                Role = QuestionUserRole.Owner
            });
        Assert.That(addAssignmentResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var dashboardResponse = await client.GetAsync("/api/tasks/my-question-assignments");
        Assert.That(dashboardResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var assignments = await dashboardResponse.Content.ReadFromJsonAsync<List<AssignedQuestionTaskResponse>>();
        Assert.That(assignments, Is.Not.Null);

        var match = assignments!.FirstOrDefault(x =>
            string.Equals(x.BidId, bidId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.QuestionId, questionId, StringComparison.OrdinalIgnoreCase));

        Assert.That(match, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(match!.BidTitle, Is.EqualTo("Dashboard Slice Co"));
            Assert.That(match.QuestionTitle, Is.EqualTo("Dashboard assignment question"));
            Assert.That(match.Role, Is.EqualTo(QuestionUserRole.Owner));
        });
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
            Company = "Dashboard Slice Co",
            Summary = "Verify dashboard question assignments endpoint",
            Questions =
            [
                new CreateQuestionRequest
                {
                    Category = "General",
                    Number = "1",
                    Title = "Dashboard assignment question",
                    Description = "Check assignment visibility",
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
