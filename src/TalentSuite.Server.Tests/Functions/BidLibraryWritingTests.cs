using Microsoft.Extensions.Logging.Abstractions;
using TalentSuite.Functions.StoringBids;
using TalentSuite.Functions.StoringBids.BidLibrary;
using TalentSuite.Functions.StoringBids.Storage;
using TalentSuite.Shared;
using TalentSuite.Shared.Bids;
using TalentSuite.Shared.Messaging.Events;

namespace TalentSuite.Server.Tests.Functions;

public class BidLibraryWriterTests
{
    [Test]
    public async Task WriteBidAsync_WritesOneBlobPerQuestion_WithExpectedNamesAndContent()
    {
        var storage = new FakeAzureBlobStorageService();
        var sut = new BidLibraryWriter(storage, NullLogger<BidLibraryWriter>.Instance);

        var bid = new BidResponse
        {
            Id = "bid-1",
            Company = "Acme / Ltd.",
            Questions =
            [
                new QuestionResponse { Id = "q1", Title = "What/Why", Number = "1", Category = "General" },
                new QuestionResponse { Id = "q2", Title = "What/Why", Number = "2", Category = "General" },
                new QuestionResponse { Id = "q3", Title = "", Number = "3/4", Category = "General" }
            ]
        };

        var answers = new Dictionary<string, string>
        {
            ["q1"] = "Answer 1",
            ["q3"] = "Answer 3"
        };

        await sut.WriteBidAsync(bid, answers, CancellationToken.None);

        Assert.That(storage.Writes, Has.Count.EqualTo(3));
        Assert.Multiple(() =>
        {
            Assert.That(storage.Writes.Select(w => w.ContainerName).Distinct(), Is.EqualTo(new[] { "bidlibrary" }));
            Assert.That(storage.Writes.Any(w => w.BlobName == "Acme - Ltd/What-Why.txt" && w.Content == "Answer 1"), Is.True);
            Assert.That(storage.Writes.Any(w => w.BlobName == "Acme - Ltd/What-Why-2.txt" && w.Content == string.Empty), Is.True);
            Assert.That(storage.Writes.Any(w => w.BlobName == "Acme - Ltd/question-3-4.txt" && w.Content == "Answer 3"), Is.True);
        });
    }

    [Test]
    public async Task WriteBidAsync_WithNoQuestions_DoesNotWriteAnyBlob()
    {
        var storage = new FakeAzureBlobStorageService();
        var sut = new BidLibraryWriter(storage, NullLogger<BidLibraryWriter>.Instance);

        await sut.WriteBidAsync(
            new BidResponse { Id = "bid-1", Company = "Acme", Questions = [] },
            new Dictionary<string, string>(),
            CancellationToken.None);

        Assert.That(storage.Writes, Is.Empty);
    }

    private sealed class FakeAzureBlobStorageService : IAzureBlobStorageService
    {
        public List<WriteCall> Writes { get; } = [];

        public Task WriteTextAsync(string containerName, string blobName, string content, CancellationToken ct = default)
        {
            Writes.Add(new WriteCall(containerName, blobName, content));
            return Task.CompletedTask;
        }

        public Task<string?> ReadTextAsync(string containerName, string blobName, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
    }

    private sealed record WriteCall(string ContainerName, string BlobName, string Content);
}

public class SaveSubmittedBidToLibraryFunctionTests
{
    [Test]
    public async Task Run_WithValidEvent_DelegatesToWriterAndSetsBidIdWhenMissing()
    {
        var writer = new FakeBidLibraryWriter();
        var sut = new SaveSubmittedBidToLibraryFunction(
            NullLogger<SaveSubmittedBidToLibraryFunction>.Instance,
            writer);

        var payload = new BidSubmittedEvent
        {
            BidId = "bid-123",
            Bid = new BidResponse
            {
                Id = null,
                Company = "Acme",
                Questions =
                [
                    new QuestionResponse { Id = "q1", Title = "Q1", Number = "1", Category = "General" }
                ]
            },
            FinalAnswerTextByQuestionId = null!
        };

        var json = System.Text.Json.JsonSerializer.Serialize(payload, SerialiserOptions.JsonOptions);

        await sut.Run(json, CancellationToken.None);

        Assert.That(writer.Calls, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(writer.Calls[0].Bid.Id, Is.EqualTo("bid-123"));
            Assert.That(writer.Calls[0].Answers, Is.Empty);
        });
    }

    [Test]
    public async Task Run_WithMissingBid_DoesNotCallWriter()
    {
        var writer = new FakeBidLibraryWriter();
        var sut = new SaveSubmittedBidToLibraryFunction(
            NullLogger<SaveSubmittedBidToLibraryFunction>.Instance,
            writer);

        var payload = new BidSubmittedEvent
        {
            BidId = "bid-123",
            Bid = null!
        };
        var json = System.Text.Json.JsonSerializer.Serialize(payload, SerialiserOptions.JsonOptions);

        await sut.Run(json, CancellationToken.None);

        Assert.That(writer.Calls, Is.Empty);
    }

    private sealed class FakeBidLibraryWriter : IBidLibraryWriter
    {
        public List<WriteCall> Calls { get; } = [];

        public Task WriteBidAsync(BidResponse bid, IReadOnlyDictionary<string, string> finalAnswerTextByQuestionId, CancellationToken ct = default)
        {
            Calls.Add(new WriteCall(bid, finalAnswerTextByQuestionId));
            return Task.CompletedTask;
        }
    }

    private sealed record WriteCall(BidResponse Bid, IReadOnlyDictionary<string, string> Answers);
}
