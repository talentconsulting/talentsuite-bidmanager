using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TalentSuite.Shared.Bids;
using TalentSuite.SliceTests.Infrastructure;

namespace TalentSuite.SliceTests.Bids;

public class Promote_red_review_to_final_answer : SliceTestBase
{
    [Test]
    public async Task PromoteRedReviewToFinal_PersistsRedReviewTextAsFinalAnswer()
    {
        var (bidId, questionId) = await CreateBidWithOneQuestionAsync();

        var redReviewText = "Reviewed and approved draft text for final submission.";
        var setRedReviewResponse = await Client.PutAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/red-review",
            new UpdateRedReviewRequest
            {
                ResultText = redReviewText,
                State = RedReviewState.Complete,
                Reviewers = new List<RedReviewReviewerResponse>()
            });
        Assert.That(setRedReviewResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var promoteToFinalResponse = await Client.PutAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/final-answer",
            new UpdateFinalAnswerRequest
            {
                AnswerText = redReviewText,
                ReadyForSubmission = true
            });
        Assert.That(promoteToFinalResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var getFinalResponse = await Client.GetAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/final-answer");
        Assert.That(getFinalResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var finalAnswer = await getFinalResponse.Content.ReadFromJsonAsync<FinalAnswerResponse>();
        Assert.That(finalAnswer, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(finalAnswer!.QuestionId, Is.EqualTo(questionId));
            Assert.That(finalAnswer.AnswerText, Is.EqualTo(redReviewText));
            Assert.That(finalAnswer.ReadyForSubmission, Is.True);
        });
    }

    [Test]
    public async Task PromoteRedReviewToFinal_CanReplaceExistingFinalAnswer()
    {
        var (bidId, questionId) = await CreateBidWithOneQuestionAsync();

        var firstFinal = "Initial final answer text.";
        var promotedFromReview = "Updated final answer promoted from red review.";

        var initialFinalResponse = await Client.PutAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/final-answer",
            new UpdateFinalAnswerRequest
            {
                AnswerText = firstFinal,
                ReadyForSubmission = false
            });
        Assert.That(initialFinalResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var setRedReviewResponse = await Client.PutAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/red-review",
            new UpdateRedReviewRequest
            {
                ResultText = promotedFromReview,
                State = RedReviewState.Complete,
                Reviewers = new List<RedReviewReviewerResponse>()
            });
        Assert.That(setRedReviewResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var promoteToFinalResponse = await Client.PutAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/final-answer",
            new UpdateFinalAnswerRequest
            {
                AnswerText = promotedFromReview,
                ReadyForSubmission = true
            });
        Assert.That(promoteToFinalResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var getFinalResponse = await Client.GetAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/final-answer");
        Assert.That(getFinalResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var finalAnswer = await getFinalResponse.Content.ReadFromJsonAsync<FinalAnswerResponse>();
        Assert.That(finalAnswer, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(finalAnswer!.AnswerText, Is.EqualTo(promotedFromReview));
            Assert.That(finalAnswer.ReadyForSubmission, Is.True);
        });
    }

    [Test]
    public async Task SetFinalAnswer_CanMarkReadyForSubmission()
    {
        var (bidId, questionId) = await CreateBidWithOneQuestionAsync();
        var finalText = "Final answer marked as ready.";

        var setResponse = await Client.PutAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/final-answer",
            new UpdateFinalAnswerRequest
            {
                AnswerText = finalText,
                ReadyForSubmission = true
            });
        Assert.That(setResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var getResponse = await Client.GetAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/final-answer");
        Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var finalAnswer = await getResponse.Content.ReadFromJsonAsync<FinalAnswerResponse>();
        Assert.That(finalAnswer, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(finalAnswer!.AnswerText, Is.EqualTo(finalText));
            Assert.That(finalAnswer.ReadyForSubmission, Is.True);
        });
    }

    [Test]
    public async Task SetFinalAnswer_CanUnsetReadyForSubmission()
    {
        var (bidId, questionId) = await CreateBidWithOneQuestionAsync();

        var setReadyResponse = await Client.PutAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/final-answer",
            new UpdateFinalAnswerRequest
            {
                AnswerText = "Initial final answer",
                ReadyForSubmission = true
            });
        Assert.That(setReadyResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var unsetReadyResponse = await Client.PutAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/final-answer",
            new UpdateFinalAnswerRequest
            {
                AnswerText = "Updated final answer after unchecking ready",
                ReadyForSubmission = false
            });
        Assert.That(unsetReadyResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var getResponse = await Client.GetAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/final-answer");
        Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var finalAnswer = await getResponse.Content.ReadFromJsonAsync<FinalAnswerResponse>();
        Assert.That(finalAnswer, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(finalAnswer!.AnswerText, Is.EqualTo("Updated final answer after unchecking ready"));
            Assert.That(finalAnswer.ReadyForSubmission, Is.False);
        });
    }

    private async Task<(string BidId, string QuestionId)> CreateBidWithOneQuestionAsync()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/bids", new CreateBidRequest
        {
            Company = "Slice Final Co",
            Summary = "Promote red review to final answer slice test bid",
            Questions =
            [
                new CreateQuestionRequest
                {
                    Category = "General",
                    Number = "1",
                    Title = "Promote red review to final",
                    Description = "Red review to final test.",
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
