using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TalentSuite.Server.Bids.Services;
using TalentSuite.Shared.Bids;
using TalentSuite.Shared.Messaging;
using TalentSuite.Shared.Messaging.Events;
using TalentSuite.SliceTests.Infrastructure;

namespace TalentSuite.SliceTests.Bids;

public class Update_bid_status : SliceTestBase
{
    [Test]
    public async Task SetBidStatus_PersistsAcrossGet()
    {
        var bidId = await CreateBidAsync();

        var setStatusResponse = await Client.PatchAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/status",
            new UpdateBidStatusRequest
            {
                Status = BidStatus.Submitted
            });
        Assert.That(setStatusResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var getResponse = await Client.GetAsync($"/api/bids/{Uri.EscapeDataString(bidId)}");
        Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var bid = await getResponse.Content.ReadFromJsonAsync<BidResponse>();
        Assert.That(bid, Is.Not.Null);
        Assert.That(bid!.Status, Is.EqualTo(BidStatus.Submitted));
    }

    [Test]
    public async Task SetBidStatus_Submitted_PublishesBidSubmittedEvent()
    {
        var bidId = await CreateBidAsync();

        var setStatusResponse = await Client.PatchAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/status",
            new UpdateBidStatusRequest
            {
                Status = BidStatus.Submitted
            });
        Assert.That(setStatusResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var bus = GetRequiredService<IAzureServiceBusClient>() as InMemoryAzureServiceBusClient;
        Assert.That(bus, Is.Not.Null);

        var publishedEvent = bus!.Messages
            .Where(x => x.EntityName == "bid-submitted")
            .Select(x => x.Payload)
            .OfType<BidSubmittedEvent>()
            .SingleOrDefault();

        Assert.That(publishedEvent, Is.Not.Null);
        Assert.That(publishedEvent!.BidId, Is.EqualTo(bidId));
        Assert.That(publishedEvent.Bid, Is.Not.Null);
        Assert.That(publishedEvent.Bid.Id, Is.EqualTo(bidId));
        Assert.That(publishedEvent.Bid.Questions, Is.Not.Empty);
        Assert.That(publishedEvent.FinalAnswerTextByQuestionId.Count, Is.EqualTo(publishedEvent.Bid.Questions.Count));
        Assert.That(
            publishedEvent.Bid.Questions.All(q => publishedEvent.FinalAnswerTextByQuestionId.ContainsKey(q.Id)),
            Is.True);
    }

    [Test]
    public async Task SetBidStatus_NonSubmitted_DoesNotPublishBidSubmittedEvent()
    {
        var bidId = await CreateBidAsync();

        var setStatusResponse = await Client.PatchAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/status",
            new UpdateBidStatusRequest
            {
                Status = BidStatus.Cancelled
            });
        Assert.That(setStatusResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var bus = GetRequiredService<IAzureServiceBusClient>() as InMemoryAzureServiceBusClient;
        Assert.That(bus, Is.Not.Null);

        var publishedSubmittedEvents = bus!.Messages
            .Where(x => x.EntityName == "bid-submitted")
            .Select(x => x.Payload)
            .OfType<BidSubmittedEvent>()
            .ToList();

        Assert.That(publishedSubmittedEvents, Is.Empty);
    }

    [Test]
    public async Task SetBidStatus_TwoSubmittedRequests_PublishesTwoBidSubmittedEvents()
    {
        var bidId = await CreateBidAsync();

        var firstResponse = await Client.PatchAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/status",
            new UpdateBidStatusRequest { Status = BidStatus.Submitted });
        Assert.That(firstResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var secondResponse = await Client.PatchAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/status",
            new UpdateBidStatusRequest { Status = BidStatus.Submitted });
        Assert.That(secondResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var bus = GetRequiredService<IAzureServiceBusClient>() as InMemoryAzureServiceBusClient;
        Assert.That(bus, Is.Not.Null);

        var publishedSubmittedEvents = bus!.Messages
            .Where(x => x.EntityName == "bid-submitted")
            .Select(x => x.Payload)
            .OfType<BidSubmittedEvent>()
            .Where(x => x.BidId == bidId)
            .ToList();

        Assert.That(publishedSubmittedEvents.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task SetBidStatus_InvalidBody_DoesNotPublishBidSubmittedEvent()
    {
        var bidId = await CreateBidAsync();
        using var payload = new StringContent(
            "{\"status\":\"not-a-valid-status\"}",
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await Client.PatchAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/status",
            payload);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var bus = GetRequiredService<IAzureServiceBusClient>() as InMemoryAzureServiceBusClient;
        Assert.That(bus, Is.Not.Null);

        var publishedSubmittedEvents = bus!.Messages
            .Where(x => x.EntityName == "bid-submitted")
            .Select(x => x.Payload)
            .OfType<BidSubmittedEvent>()
            .ToList();

        Assert.That(publishedSubmittedEvents, Is.Empty);
    }

    [Test]
    public async Task SetBidStatus_Submitted_WithExistingBidLibraryPush_DoesNotPublishBidSubmittedEvent()
    {
        var bidId = await CreateBidAsync();
        using (var scope = Factory.Services.CreateScope())
        {
            var bidService = scope.ServiceProvider.GetRequiredService<IBidService>();
            _ = await bidService.PushBidToLibrary(
                bidId,
                "user-1",
                "User One",
                DateTime.UtcNow);
        }

        var setStatusResponse = await Client.PatchAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/status",
            new UpdateBidStatusRequest { Status = BidStatus.Submitted });
        Assert.That(setStatusResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var bus = GetRequiredService<IAzureServiceBusClient>() as InMemoryAzureServiceBusClient;
        Assert.That(bus, Is.Not.Null);

        var publishedSubmittedEvents = bus!.Messages
            .Where(x => x.EntityName == "bid-submitted")
            .Select(x => x.Payload)
            .OfType<BidSubmittedEvent>()
            .Where(x => x.BidId == bidId)
            .ToList();

        Assert.That(publishedSubmittedEvents, Is.Empty);
    }

    [Test]
    public async Task SetBidStatus_Submitted_MarksAllQuestionCommentsComplete()
    {
        var bidId = await CreateBidAsync();
        var bid = await GetBidAsync(bidId);
        var questionId = bid.Questions[0].Id;

        var addDraftResponse = await Client.PostAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/drafts",
            new DraftRequest { Response = "Initial draft response." });
        Assert.That(addDraftResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var draft = await addDraftResponse.Content.ReadFromJsonAsync<CreateAssetResponse>();
        Assert.That(draft, Is.Not.Null);

        var addDraftCommentResponse = await Client.PostAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/drafts/{Uri.EscapeDataString(draft!.Id)}/comments",
            new AddDraftCommentRequest
            {
                Comment = "Draft comment",
                UserId = "draft-user",
                AuthorName = "Draft Author"
            });
        Assert.That(addDraftCommentResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var createdDraftComment = await addDraftCommentResponse.Content.ReadFromJsonAsync<DraftCommentResponse>();
        Assert.That(createdDraftComment, Is.Not.Null);
        Assert.That(createdDraftComment!.IsComplete, Is.False);

        var addRedReviewCommentResponse = await Client.PostAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/red-review/comments",
            new AddDraftCommentRequest
            {
                Comment = "Red review comment",
                UserId = "review-user",
                AuthorName = "Review Author"
            });
        Assert.That(addRedReviewCommentResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var createdRedReviewComment = await addRedReviewCommentResponse.Content.ReadFromJsonAsync<DraftCommentResponse>();
        Assert.That(createdRedReviewComment, Is.Not.Null);
        Assert.That(createdRedReviewComment!.IsComplete, Is.False);

        var addFinalAnswerCommentResponse = await Client.PostAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/final-answer/comments",
            new AddDraftCommentRequest
            {
                Comment = "Final answer comment",
                UserId = "final-user",
                AuthorName = "Final Author"
            });
        Assert.That(addFinalAnswerCommentResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var createdFinalAnswerComment = await addFinalAnswerCommentResponse.Content.ReadFromJsonAsync<DraftCommentResponse>();
        Assert.That(createdFinalAnswerComment, Is.Not.Null);
        Assert.That(createdFinalAnswerComment!.IsComplete, Is.False);

        var setStatusResponse = await Client.PatchAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/status",
            new UpdateBidStatusRequest { Status = BidStatus.Submitted });
        Assert.That(setStatusResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var draftCommentsResponse = await Client.GetAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/drafts/{Uri.EscapeDataString(draft.Id)}/comments");
        Assert.That(draftCommentsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var draftComments = await draftCommentsResponse.Content.ReadFromJsonAsync<List<DraftCommentResponse>>();
        Assert.That(draftComments, Is.Not.Null);
        Assert.That(draftComments!.Any(c => c.Id == createdDraftComment.Id), Is.True);
        Assert.That(draftComments.All(c => c.IsComplete), Is.True);

        var redReviewResponse = await Client.GetAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/red-review");
        Assert.That(redReviewResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var redReview = await redReviewResponse.Content.ReadFromJsonAsync<RedReviewResponse>();
        Assert.That(redReview, Is.Not.Null);
        Assert.That(redReview!.Comments.Any(c => c.Id == createdRedReviewComment.Id), Is.True);
        Assert.That(redReview.Comments.All(c => c.IsComplete), Is.True);

        var finalAnswerResponse = await Client.GetAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/final-answer");
        Assert.That(finalAnswerResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var finalAnswer = await finalAnswerResponse.Content.ReadFromJsonAsync<FinalAnswerResponse>();
        Assert.That(finalAnswer, Is.Not.Null);
        Assert.That(finalAnswer!.Comments.Any(c => c.Id == createdFinalAnswerComment.Id), Is.True);
        Assert.That(finalAnswer.Comments.All(c => c.IsComplete), Is.True);
    }

    private async Task<BidResponse> GetBidAsync(string bidId)
    {
        var getResponse = await Client.GetAsync($"/api/bids/{Uri.EscapeDataString(bidId)}");
        Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var bid = await getResponse.Content.ReadFromJsonAsync<BidResponse>();
        Assert.That(bid, Is.Not.Null);
        Assert.That(bid!.Questions, Is.Not.Empty);
        return bid;
    }

    private async Task<string> CreateBidAsync()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/bids", new CreateBidRequest
        {
            Company = "Slice Status Co",
            Summary = "Status update slice test bid",
            Questions =
            [
                new CreateQuestionRequest
                {
                    Category = "General",
                    Number = "1",
                    Title = "Status question",
                    Description = "Status update test question.",
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
        return bidId!;
    }
}
