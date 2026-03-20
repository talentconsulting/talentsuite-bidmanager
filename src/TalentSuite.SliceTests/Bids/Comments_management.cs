using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TalentSuite.Shared.Bids;
using TalentSuite.SliceTests.Infrastructure;

namespace TalentSuite.SliceTests.Bids;

public class Comments_management : SliceTestBase
{
    [Test]
    public async Task AddRedReviewComment_CreatesComment()
    {
        var (bidId, questionId) = await CreateBidWithOneQuestionAsync();

        var addResponse = await Client.PostAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/red-review/comments",
            new AddDraftCommentRequest
            {
                Comment = "Please tighten this wording.",
                UserId = "04d3fde7-8b47-4558-905b-1888fb8a4db0",
                AuthorName = "Richard Parkins",
                StartIndex = 0,
                EndIndex = 10,
                SelectedText = "Draft text"
            });

        Assert.That(addResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var comment = await addResponse.Content.ReadFromJsonAsync<DraftCommentResponse>();
        Assert.That(comment, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(comment!.Id, Is.Not.Empty);
            Assert.That(comment.Comment, Is.EqualTo("Please tighten this wording."));
            Assert.That(comment.IsComplete, Is.False);
            Assert.That(comment.UserId, Is.EqualTo("04d3fde7-8b47-4558-905b-1888fb8a4db0"));
        });
    }

    [Test]
    public async Task CompleteRedReviewComment_MarksCommentAsComplete()
    {
        var (bidId, questionId) = await CreateBidWithOneQuestionAsync();

        var addResponse = await Client.PostAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/red-review/comments",
            new AddDraftCommentRequest
            {
                Comment = "Resolve this before final submission.",
                UserId = "04d3fde7-8b47-4558-905b-1888fb8a4db0",
                AuthorName = "Richard Parkins"
            });
        Assert.That(addResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var createdComment = await addResponse.Content.ReadFromJsonAsync<DraftCommentResponse>();
        Assert.That(createdComment, Is.Not.Null);

        var completeResponse = await Client.PatchAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/red-review/comments/{Uri.EscapeDataString(createdComment!.Id)}",
            new SetCommentCompletionRequest { IsComplete = true });

        Assert.That(completeResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var completed = await completeResponse.Content.ReadFromJsonAsync<DraftCommentResponse>();
        Assert.That(completed, Is.Not.Null);
        Assert.That(completed!.IsComplete, Is.True);

        var reviewResponse = await Client.GetAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/red-review");
        Assert.That(reviewResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var review = await reviewResponse.Content.ReadFromJsonAsync<RedReviewResponse>();
        Assert.That(review, Is.Not.Null);
        Assert.That(review!.Comments.Any(c => c.Id == createdComment.Id && c.IsComplete), Is.True);
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
