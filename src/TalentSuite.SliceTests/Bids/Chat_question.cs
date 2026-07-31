using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TalentSuite.Server.Bids.Services;
using TalentSuite.Shared.Bids;
using TalentSuite.Shared.Bids.Ai;
using TalentSuite.SliceTests.Infrastructure;

namespace TalentSuite.SliceTests.Bids;

public class Chat_question
{
    [Test]
    public async Task AskQuestion_IncludesOnlySelectedBidFiles()
    {
        using var factory = new AuthenticatedTestWebApplicationFactory();
        using var client = factory.CreateClient();

        SetIdentityHeaders(client, subject: "admin-seed", username: "admin-seed", roles: "admin,user");

        var (bidId, questionId) = await CreateBidWithOneQuestionAsync(client);
        var selectedFile = await UploadBidFileAsync(client, bidId, "selected-notes.docx");
        await UploadBidFileAsync(client, bidId, "unchecked-notes.docx");

        var response = await client.PostAsJsonAsync(
            $"/api/ai/questions/{Uri.EscapeDataString(questionId)}",
            new ChatQuestionRequest
            {
                BidId = bidId,
                QuestionId = questionId,
                FreeTextQuestion = "Use the selected attachment.",
                BidFileIds = [selectedFile.Id]
            });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var chatService = (InMemoryAzureOpenAiChatService)factory.Services
            .GetRequiredService<IAzureOpenAiChatService>();
        Assert.That(chatService.LastSystemPrompt, Does.Contain("Attachment: selected-notes.docx"));
        Assert.That(chatService.LastSystemPrompt, Does.Not.Contain("unchecked-notes.docx"));
    }

    [Test]
    public async Task AskQuestion_Admin_ReusesThreadForConversation()
    {
        using var factory = new AuthenticatedTestWebApplicationFactory();
        using var client = factory.CreateClient();

        SetIdentityHeaders(client, subject: "admin-seed", username: "admin-seed", roles: "admin,user");

        var (bidId, questionId) = await CreateBidWithOneQuestionAsync(client);

        var firstResponse = await client.PostAsJsonAsync(
            $"/api/ai/questions/{Uri.EscapeDataString(questionId)}",
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
        Assert.That(firstPayload.Sources, Has.Count.EqualTo(1));
        Assert.That(firstPayload.Sources[0].FileName, Is.EqualTo("stub-bid-library.md"));
        Assert.That(firstPayload.UsedSourcesOutsideBidLibrary, Is.False);

        var secondResponse = await client.PostAsJsonAsync(
            $"/api/ai/questions/{Uri.EscapeDataString(questionId)}",
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

        var messagesResponse = await client.GetAsync(
            $"/api/ai/questions/{Uri.EscapeDataString(questionId)}/messages?bidId={Uri.EscapeDataString(bidId)}");
        Assert.That(messagesResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var messages = await messagesResponse.Content.ReadFromJsonAsync<List<ChatMessageResponse>>();
        Assert.That(messages, Is.Not.Null);

        var assistantMessage = messages!
            .LastOrDefault(message => string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase));
        Assert.That(assistantMessage, Is.Not.Null);
        Assert.That(assistantMessage!.Sources, Has.Count.EqualTo(1));
        Assert.That(assistantMessage.Sources[0].FileName, Is.EqualTo("stub-bid-library.md"));
        Assert.That(assistantMessage.UsedSourcesOutsideBidLibrary, Is.False);
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

    private static async Task<BidFileResponse> UploadBidFileAsync(HttpClient client, string bidId, string fileName)
    {
        using var multipart = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes($"Contents of {fileName}"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        multipart.Add(fileContent, "file", fileName);

        var response = await client.PostAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/files",
            multipart);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var uploaded = await response.Content.ReadFromJsonAsync<BidFileResponse>();
        Assert.That(uploaded, Is.Not.Null);
        return uploaded!;
    }

    [Test]
    public async Task AskQuestion_ReturnsBadRequest_WhenRouteAndBodyQuestionIdsDiffer()
    {
        using var factory = new AuthenticatedTestWebApplicationFactory();
        using var client = factory.CreateClient();

        SetIdentityHeaders(client, subject: "admin-seed", username: "admin-seed", roles: "admin,user");

        var (bidId, questionId) = await CreateBidWithOneQuestionAsync(client);

        var response = await client.PostAsJsonAsync(
            "/api/ai/questions/not-the-same-id",
            new ChatQuestionRequest
            {
                BidId = bidId,
                QuestionId = questionId,
                FreeTextQuestion = "How should we answer this?"
            });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }
}
