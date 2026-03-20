using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TalentSuite.Shared.Bids;
using TalentSuite.Shared.Messaging.Events;
using TalentSuite.SliceTests.Infrastructure;

namespace TalentSuite.SliceTests.Bids;

public class Comment_saved_with_mentions : SliceTestBase
{
    [Test]
    public async Task AddDraftComment_WithMentions_PublishesCommentSavedWithMentionsEvent()
    {
        var (bidId, questionId) = await CreateBidWithOneQuestionAsync();
        var draftId = await CreateDraftAsync(bidId, questionId);

        var addResponse = await Client.PostAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/drafts/{Uri.EscapeDataString(draftId)}/comments",
            new AddDraftCommentRequest
            {
                Comment = "Draft comment with mention.",
                UserId = "04d3fde7-8b47-4558-905b-1888fb8a4db0",
                AuthorName = "Richard Parkins",
                MentionedUserIds = ["0cf878f8-0840-4e1f-81af-983462b73722"]
            });

        Assert.That(addResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var saved = await addResponse.Content.ReadFromJsonAsync<DraftCommentResponse>();
        Assert.That(saved, Is.Not.Null);

        var published = FindPublishedMentionEvent();
        Assert.That(published, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(published!.BidId, Is.EqualTo(bidId));
            Assert.That(published.QuestionId, Is.EqualTo(questionId));
            Assert.That(published.CommentId, Is.EqualTo(saved!.Id));
            Assert.That(published.Tab, Is.EqualTo("drafts"));
            Assert.That(published.Comment, Is.EqualTo("Draft comment with mention."));
            Assert.That(published.MentionedUsers, Has.Count.EqualTo(1));
            Assert.That(published.MentionedUsers[0].FullName, Is.EqualTo("Karen Spearing"));
            Assert.That(published.MentionedUsers[0].Email, Is.EqualTo("karen.spearing@hotmail.com"));
            Assert.That(published.QuestionLink, Does.Contain($"/bids/manage/{bidId}"));
            Assert.That(published.QuestionLink, Does.Contain($"questionId={questionId}"));
            Assert.That(published.QuestionLink, Does.Contain("tab=drafts"));
            Assert.That(published.QuestionLink, Does.Contain($"commentId={saved.Id}"));
        });
    }

    [Test]
    public async Task AddRedReviewComment_WithMentions_PublishesCommentSavedWithMentionsEvent()
    {
        var (bidId, questionId) = await CreateBidWithOneQuestionAsync();

        var addResponse = await Client.PostAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/red-review/comments",
            new AddDraftCommentRequest
            {
                Comment = "Review comment with mention.",
                UserId = "04d3fde7-8b47-4558-905b-1888fb8a4db0",
                AuthorName = "Richard Parkins",
                MentionedUserIds = ["0cf878f8-0840-4e1f-81af-983462b73722"]
            });

        Assert.That(addResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var saved = await addResponse.Content.ReadFromJsonAsync<DraftCommentResponse>();
        Assert.That(saved, Is.Not.Null);

        var published = FindPublishedMentionEvent();
        Assert.That(published, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(published!.Tab, Is.EqualTo("review"));
            Assert.That(published.CommentId, Is.EqualTo(saved!.Id));
            Assert.That(published.QuestionLink, Does.Contain("tab=review"));
        });
    }

    [Test]
    public async Task AddFinalAnswerComment_WithMentions_PublishesCommentSavedWithMentionsEvent()
    {
        var (bidId, questionId) = await CreateBidWithOneQuestionAsync();

        var addResponse = await Client.PostAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/final-answer/comments",
            new AddDraftCommentRequest
            {
                Comment = "Final comment with mention.",
                UserId = "04d3fde7-8b47-4558-905b-1888fb8a4db0",
                AuthorName = "Richard Parkins",
                MentionedUserIds = ["0cf878f8-0840-4e1f-81af-983462b73722"]
            });

        Assert.That(addResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var saved = await addResponse.Content.ReadFromJsonAsync<DraftCommentResponse>();
        Assert.That(saved, Is.Not.Null);

        var published = FindPublishedMentionEvent();
        Assert.That(published, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(published!.Tab, Is.EqualTo("final-answer"));
            Assert.That(published.CommentId, Is.EqualTo(saved!.Id));
            Assert.That(published.QuestionLink, Does.Contain("tab=final-answer"));
        });
    }

    private CommentSavedWithMentionsEvent? FindPublishedMentionEvent()
    {
        var bus = GetRequiredService<TalentSuite.Shared.Messaging.IAzureServiceBusClient>() as InMemoryAzureServiceBusClient;
        Assert.That(bus, Is.Not.Null);

        return bus!.Messages
            .Where(x => x.EntityName == "comment-saved-with-mentions")
            .Select(x => x.Payload)
            .OfType<CommentSavedWithMentionsEvent>()
            .SingleOrDefault();
    }

    private async Task<string> CreateDraftAsync(string bidId, string questionId)
    {
        var addDraftResponse = await Client.PostAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/drafts",
            new DraftRequest
            {
                Response = "Initial draft response."
            });
        Assert.That(addDraftResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var draft = await addDraftResponse.Content.ReadFromJsonAsync<CreateAssetResponse>();
        Assert.That(draft, Is.Not.Null);
        Assert.That(string.IsNullOrWhiteSpace(draft!.Id), Is.False);
        return draft.Id;
    }

    private async Task<(string BidId, string QuestionId)> CreateBidWithOneQuestionAsync()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/bids", new CreateBidRequest
        {
            Company = "Slice Co",
            Summary = "Slice test bid",
            Questions =
            [
                new CreateQuestionRequest
                {
                    Category = "General",
                    Number = "1",
                    Title = "How do you deliver value?",
                    Description = "Describe your approach.",
                    Length = "300 words",
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

        var bidResponse = await Client.GetAsync($"/api/bids/{Uri.EscapeDataString(bidId!)}");
        Assert.That(bidResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var bid = await bidResponse.Content.ReadFromJsonAsync<BidResponse>();
        Assert.That(bid, Is.Not.Null);
        Assert.That(bid!.Questions, Is.Not.Empty);
        var questionId = bid.Questions[0].Id;
        Assert.That(string.IsNullOrWhiteSpace(questionId), Is.False);

        return (bidId!, questionId);
    }
}
