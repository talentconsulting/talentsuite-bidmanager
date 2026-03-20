using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using TalentSuite.Shared;
using TalentSuite.Shared.Messaging.Events;

namespace TalentSuite.Functions.CommentEmail;

public class CommentSavedWithMentionsFunction(
    ILogger<CommentSavedWithMentionsFunction> logger,
    ICommentMentionEmailSender mentionEmailSender)
{
    private const string QueueName = "comment-saved-with-mentions";

    [Function("CommentSavedWithMentionsFunction")]
    public async Task Run(
        [ServiceBusTrigger("comment-saved-with-mentions", Connection = "AzureWebJobsServiceBus")]
        string eventJson,
        CancellationToken ct)
    {
        CommentSavedWithMentionsEvent? @event = null;

        try
        {
            @event = JsonSerializer.Deserialize<CommentSavedWithMentionsEvent>(eventJson, SerialiserOptions.JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to deserialize CommentSavedWithMentions event payload: {Payload}", eventJson);
            throw;
        }

        if (@event is null || string.IsNullOrWhiteSpace(@event.BidId) || string.IsNullOrWhiteSpace(@event.QuestionId))
        {
            logger.LogWarning("CommentSavedWithMentions event missing required data. Payload: {Payload}", eventJson);
            return;
        }

        logger.LogInformation(
            "Handling {MessageType} from queue {QueueName}",
            nameof(CommentSavedWithMentionsEvent),
            QueueName);

        var recipients = (@event.MentionedUsers ?? new List<CommentMentionedUser>())
            .Where(u => !string.IsNullOrWhiteSpace(u.Email))
            .GroupBy(u => u.Email, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        if (recipients.Count == 0)
        {
            logger.LogInformation(
                "CommentSavedWithMentions event for BidId {BidId}, QuestionId {QuestionId} has no valid recipients.",
                @event.BidId,
                @event.QuestionId);
            return;
        }

        foreach (var recipient in recipients)
        {
            await mentionEmailSender.SendCommentMentionAsync(@event, recipient, ct);
        }
    }
}
