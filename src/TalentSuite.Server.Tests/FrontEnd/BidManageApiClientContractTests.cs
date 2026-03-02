using System.Net;
using System.Net.Http;
using System.Text;
using TalentSuite.FrontEnd.Pages.Bids.Management;

namespace TalentSuite.Server.Tests.FrontEnd;

public class BidManageApiClientContractTests
{
    [Test]
    public async Task GetBidAsync_DeserializesStringQuestionNumber()
    {
        const string json = """
                            {
                              "id": "bid-1",
                              "company": "Talent Consulting",
                              "questions": [
                                {
                                  "id": "q-1",
                                  "questionOrderIndex": 3,
                                  "number": "1.1",
                                  "title": "Question A",
                                  "description": "Desc",
                                  "length": "500 words",
                                  "weighting": 10,
                                  "required": true,
                                  "niceToHave": false
                                }
                              ]
                            }
                            """;

        using var http = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }))
        {
            BaseAddress = new Uri("https://localhost/")
        };

        var client = new BidManageApiClient(http);
        var bid = await client.GetBidAsync("bid-1");

        Assert.That(bid, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(bid!.Id, Is.EqualTo("bid-1"));
            Assert.That(bid.Questions, Has.Count.EqualTo(1));
            Assert.That(bid.Questions[0].QuestionOrderIndex, Is.EqualTo(3));
            Assert.That(bid.Questions[0].Number, Is.EqualTo("1.1"));
        });
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
