using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TalentSuite.Shared.Bids;
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
