using TalentSuite.FrontEnd;
using TalentSuite.Server.Bids.Services;
using TalentSuite.Shared.Bids;

namespace TalentSuite.Server.Tests.Bids;

public class QuestionOrderIndexTests
{
    [Test]
    public async Task InMemoryDocumentIngestion_AssignsSequentialQuestionOrderIndexStartingAtOne()
    {
        var sut = new InMemoryDocumentIngestionService();
        await using var stream = new MemoryStream([1, 2, 3]);

        var parsed = await sut.ExtractDocumentAsync(stream, "dummy.pdf", BidStage.Stage1, CancellationToken.None);

        Assert.That(parsed, Is.Not.Null);
        Assert.That(parsed!.Questions, Is.Not.Empty);

        var expected = Enumerable.Range(1, parsed.Questions.Count).ToArray();
        var actual = parsed.Questions.Select(q => q.QuestionOrderIndex).ToArray();

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void ClientResponseModel_Sort_OrdersQuestionsByQuestionOrderIndex()
    {
        var response = new ParsedDocumentResponse
        {
            Questions =
            [
                new ParsedQuestionResponse { QuestionOrderIndex = 3, Category = "B", Number = "x" },
                new ParsedQuestionResponse { QuestionOrderIndex = 1, Category = "A", Number = "z" },
                new ParsedQuestionResponse { QuestionOrderIndex = 2, Category = "C", Number = "y" }
            ]
        };

        var model = new ClientResponseModel(response);

        model.Sort();

        Assert.That(model.Response.Questions.Select(q => q.QuestionOrderIndex), Is.EqualTo(new[] { 1, 2, 3 }));
    }
}
