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

public class Push_bid_to_library : SliceTestBase
{
    [Test]
    public async Task PushBidToLibrary_PersistsPushMetadataOnBid()
    {
        var bidId = await CreateBidAsync();

        var pushResponse = await Client.PostAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/library-push",
            null);
        Assert.That(pushResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var push = await pushResponse.Content.ReadFromJsonAsync<BidLibraryPushResponse>();
        Assert.That(push, Is.Not.Null);
        Assert.That(string.IsNullOrWhiteSpace(push!.BidId), Is.False);

        var getResponse = await Client.GetAsync($"/api/bids/{Uri.EscapeDataString(bidId)}");
        Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var bid = await getResponse.Content.ReadFromJsonAsync<BidResponse>();
        Assert.That(bid, Is.Not.Null);
        Assert.That(bid!.BidLibraryPush, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(bid.BidLibraryPush!.BidId, Is.EqualTo(bidId));
            Assert.That(string.IsNullOrWhiteSpace(bid.BidLibraryPush.PerformedByUserId), Is.False);
            Assert.That(bid.BidLibraryPush.PushedAtUtc, Is.Not.EqualTo(default(DateTime)));
        });
    }

    [Test]
    public async Task PushBidToLibrary_PublishesBidSubmittedEvent()
    {
        var bidId = await CreateBidAsync();

        var pushResponse = await Client.PostAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/library-push",
            null);
        Assert.That(pushResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

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
    public async Task PushBidToLibrary_SecondPush_ReturnsExistingMetadata()
    {
        var bidId = await CreateBidAsync();

        BidLibraryPushResponse? firstPush;
        using (var scope = Factory.Services.CreateScope())
        {
            var bidService = scope.ServiceProvider.GetRequiredService<IBidService>();
            firstPush = await bidService.PushBidToLibrary(
                bidId,
                "user-1",
                "User One",
                new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc));
        }

        BidLibraryPushResponse? secondPush;
        using (var scope = Factory.Services.CreateScope())
        {
            var bidService = scope.ServiceProvider.GetRequiredService<IBidService>();
            secondPush = await bidService.PushBidToLibrary(
                bidId,
                "user-2",
                "User Two",
                new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc));
        }

        Assert.That(firstPush, Is.Not.Null);
        Assert.That(secondPush, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(secondPush!.BidId, Is.EqualTo(firstPush!.BidId));
            Assert.That(secondPush.PerformedByUserId, Is.EqualTo(firstPush.PerformedByUserId));
            Assert.That(secondPush.PerformedByName, Is.EqualTo(firstPush.PerformedByName));
            Assert.That(secondPush.PushedAtUtc, Is.EqualTo(firstPush.PushedAtUtc));
        });
    }

    private async Task<string> CreateBidAsync()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/bids", new CreateBidRequest
        {
            Company = "Slice Push Co",
            Summary = "Push bid to library slice test bid",
            Questions =
            [
                new CreateQuestionRequest
                {
                    Category = "General",
                    Number = "1",
                    Title = "Push bid question",
                    Description = "Push bid question description.",
                    Length = "250 words",
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
