using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using TalentSuite.FrontEnd.Pages.Bids;
using TalentSuite.Shared.Bids;
using TalentSuite.Shared.Bids.List;
using TalentSuite.Shared.Users;

namespace TalentSuite.Server.Tests.FrontEnd.Bids;

[TestFixture]
public sealed class HomePaginationFilteringTests : Bunit.TestContext
{
    [Test]
    public void HidesPaginationWhenFilteredItemsAreLessThanPageSize()
    {
        var bids = new PagedBidListResponse
        {
            CurrentPage = 1,
            PageSize = 10,
            TotalCount = 11,
            Items = new List<BidListItemResponse>
            {
                CreateBid("1", BidStatus.Underway),
                CreateBid("2", BidStatus.Submitted),
                CreateBid("3", BidStatus.Underway),
                CreateBid("4", BidStatus.Submitted),
                CreateBid("5", BidStatus.Underway),
                CreateBid("6", BidStatus.Underway),
                CreateBid("7", BidStatus.Submitted),
                CreateBid("8", BidStatus.Underway),
                CreateBid("9", BidStatus.Underway),
                CreateBid("10", BidStatus.Underway)
            }
        };

        Services.AddSingleton(new HttpClient(new StubHttpMessageHandler(req =>
        {
            if (req.RequestUri?.PathAndQuery == "/api/users/me-authorisation")
                return JsonResponse(new CurrentUserAuthorisationResponse { IsAdmin = true });

            if (req.RequestUri?.PathAndQuery == "/api/bids?page=1&pageSize=10")
                return JsonResponse(bids);

            throw new InvalidOperationException($"Unexpected request: {req.RequestUri}");
        }))
        {
            BaseAddress = new Uri("https://localhost")
        });

        var cut = Render<Home>();

        cut.WaitForState(() => cut.FindAll("select").Count == 1);
        cut.Find("select").Change("Submitted");

        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.FindAll("nav[aria-label='Bids pagination']").Count, Is.EqualTo(0));
            Assert.That(cut.Markup, Does.Not.Contain("Next →"));
            Assert.That(cut.Markup, Does.Not.Contain("← Prev"));
        });
    }

    private static BidListItemResponse CreateBid(string id, BidStatus status)
        => new()
        {
            Id = id,
            Company = $"Company {id}",
            Summary = $"Summary {id}",
            QuestionCount = 3,
            OwnerId = "owner",
            Status = status
        };

    private static HttpResponseMessage JsonResponse<T>(T payload)
        => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
