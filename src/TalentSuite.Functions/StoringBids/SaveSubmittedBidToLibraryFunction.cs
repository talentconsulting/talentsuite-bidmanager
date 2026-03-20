using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using TalentSuite.Functions.StoringBids.BidLibrary;
using TalentSuite.Shared;
using TalentSuite.Shared.Messaging.Events;

namespace TalentSuite.Functions.StoringBids;

public class SaveSubmittedBidToLibraryFunction(
    ILogger<SaveSubmittedBidToLibraryFunction> logger,
    IBidLibraryWriter bidLibraryWriter)
{
    private const string QueueName = "bid-submitted";

    [Function("BidSubmittedFunction")]
    public async Task Run(
        [ServiceBusTrigger("bid-submitted", Connection = "AzureWebJobsServiceBus")]
        string eventJson,
        CancellationToken ct)
    {
        BidSubmittedEvent? @event = null;

        try
        {
            @event = JsonSerializer.Deserialize<BidSubmittedEvent>(eventJson, SerialiserOptions.JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to deserialize BidSubmitted event payload: {Payload}", eventJson);
            throw;
        }

        if (@event?.Bid is null || string.IsNullOrWhiteSpace(@event.BidId))
        {
            logger.LogWarning("BidSubmitted event missing required data. Payload: {Payload}", eventJson);
            return;
        }

        logger.LogInformation(
            "Handling {MessageType} from queue {QueueName}",
            nameof(BidSubmittedEvent),
            QueueName);

        logger.LogInformation("Received BidSubmitted event for BidId {BidId}.", @event.BidId);

        @event.Bid.Id ??= @event.BidId;

        var finalAnswerTextByQuestionId = @event.FinalAnswerTextByQuestionId
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        await bidLibraryWriter.WriteBidAsync(@event.Bid, finalAnswerTextByQuestionId, ct);
    }
}
