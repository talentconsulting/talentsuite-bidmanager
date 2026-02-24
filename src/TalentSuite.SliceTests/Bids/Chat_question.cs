using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TalentSuite.Shared.Bids;
using TalentSuite.Shared.Bids.Ai;
using TalentSuite.SliceTests.Infrastructure;

namespace TalentSuite.SliceTests.Bids;

public class Chat_question
{
    [Test]
    public async Task AskQuestion_Admin_ReusesThreadForConversation()
    {
        using var factory = new AuthenticatedTestWebApplicationFactory();
        using var client = factory.CreateClient();

        SetIdentityHeaders(client, subject: "admin-seed", username: "admin-seed", roles: "admin,user");

        var (bidId, questionId) = await CreateBidWithOneQuestionAsync(client);

        var firstResponse = await client.PostAsJsonAsync(
            "/api/ai/questions/anything",
            new ChatQuestionRequest
            {
                BidId = bidId,
                QuestionId = questionId,
                FreeTextQuestion = "How should we answer this?"
            });

        Assert.That(firstResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var firstPayload = await firstResponse.Content.ReadFromJsonAsync<ChatQuestionResponse>();
        Assert.That(firstPayload, Is.Not.Null);
        Assert.That(firstPayload!.Response, Is.EqualTo("[stubbed-chat] How should we answer this?"));
        Assert.That(string.IsNullOrWhiteSpace(firstPayload.ThreadId), Is.False);

        var secondResponse = await client.PostAsJsonAsync(
            "/api/ai/questions/anything",
            new ChatQuestionRequest
            {
                BidId = bidId,
                QuestionId = questionId,
                FreeTextQuestion = "And what evidence supports that?"
            });

        Assert.That(secondResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var secondPayload = await secondResponse.Content.ReadFromJsonAsync<ChatQuestionResponse>();
        Assert.That(secondPayload, Is.Not.Null);
        Assert.That(secondPayload!.Response, Is.EqualTo("[stubbed-chat] And what evidence supports that?"));
        Assert.That(secondPayload.ThreadId, Is.EqualTo(firstPayload.ThreadId));
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
            Company = "Slice AI Co",
            Summary = "AI question slice test bid",
            Questions =
            [
                new CreateQuestionRequest
                {
                    Category = "General",
                    Number = "1",
                    Title = "AI question",
                    Description = "What is our answer approach?",
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
