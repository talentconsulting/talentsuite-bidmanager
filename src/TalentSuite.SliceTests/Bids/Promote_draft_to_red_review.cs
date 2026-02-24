using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TalentSuite.Shared.Bids;
using TalentSuite.SliceTests.Infrastructure;

namespace TalentSuite.SliceTests.Bids;

public class Promote_draft_to_red_review : SliceTestBase
{
    [Test]
    public async Task PromoteDraftToRedReview_PersistsDraftTextAsRedReviewResult()
    {
        var (bidId, questionId) = await CreateBidWithOneQuestionAsync();

        var draftText = "This is the draft that should be promoted to red review.";
        var addDraftResponse = await Client.PostAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/drafts",
            new DraftRequest { Response = draftText });
        Assert.That(addDraftResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var promoteResponse = await Client.PutAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/red-review",
            new UpdateRedReviewRequest
            {
                ResultText = draftText,
                State = RedReviewState.Pending,
                Reviewers = new List<RedReviewReviewerResponse>()
            });
        Assert.That(promoteResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var getReviewResponse = await Client.GetAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/red-review");
        Assert.That(getReviewResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var review = await getReviewResponse.Content.ReadFromJsonAsync<RedReviewResponse>();
        Assert.That(review, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(review!.QuestionId, Is.EqualTo(questionId));
            Assert.That(review.ResultText, Is.EqualTo(draftText));
            Assert.That(review.State, Is.EqualTo(RedReviewState.Pending));
        });
    }

    [Test]
    public async Task PromoteDraftToRedReview_CanReplaceExistingPromotedText()
    {
        var (bidId, questionId) = await CreateBidWithOneQuestionAsync();

        var firstDraft = "First promoted draft.";
        var secondDraft = "Second promoted draft should overwrite first.";

        var addFirstDraftResponse = await Client.PostAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/drafts",
            new DraftRequest { Response = firstDraft });
        Assert.That(addFirstDraftResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var firstPromoteResponse = await Client.PutAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/red-review",
            new UpdateRedReviewRequest
            {
                ResultText = firstDraft,
                State = RedReviewState.Pending,
                Reviewers = new List<RedReviewReviewerResponse>()
            });
        Assert.That(firstPromoteResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var addSecondDraftResponse = await Client.PostAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/drafts",
            new DraftRequest { Response = secondDraft });
        Assert.That(addSecondDraftResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var secondPromoteResponse = await Client.PutAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/red-review",
            new UpdateRedReviewRequest
            {
                ResultText = secondDraft,
                State = RedReviewState.Pending,
                Reviewers = new List<RedReviewReviewerResponse>()
            });
        Assert.That(secondPromoteResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var getReviewResponse = await Client.GetAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/red-review");
        Assert.That(getReviewResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var review = await getReviewResponse.Content.ReadFromJsonAsync<RedReviewResponse>();
        Assert.That(review, Is.Not.Null);
        Assert.That(review!.ResultText, Is.EqualTo(secondDraft));
    }

    private async Task<(string BidId, string QuestionId)> CreateBidWithOneQuestionAsync()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/bids", new CreateBidRequest
        {
            Company = "Slice Promote Co",
            Summary = "Promote draft slice test bid",
            Questions =
            [
                new CreateQuestionRequest
                {
                    Category = "General",
                    Number = "1",
                    Title = "Promote this draft",
                    Description = "Draft to red review test.",
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
