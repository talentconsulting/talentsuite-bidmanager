using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using TalentSuite.Shared;
using TalentSuite.Shared.Messaging.Commands;

namespace TalentSuite.Functions.InviteEmail;

public class InviteUserFunction(
    ILogger<InviteUserFunction> logger,
    IInviteEmailSender inviteEmailSender)
{
    private const string QueueName = "invite-user";

    [Function("InviteUserFunction")]
    public async Task Run(
        [ServiceBusTrigger("invite-user", Connection = "AzureWebJobsServiceBus")]
        string commandJson,
        CancellationToken ct)
    {
        InviteUserCommand? command = null;

        try
        {
            command = JsonSerializer.Deserialize<InviteUserCommand>(commandJson, SerialiserOptions.JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogError(
                ex,
                "Failed to deserialize InviteUser command payload. PayloadLength={PayloadLength}.",
                commandJson?.Length ?? 0);
            throw;
        }

        if (command is null || string.IsNullOrWhiteSpace(command.UserId) || string.IsNullOrWhiteSpace(command.Email))
        {
            logger.LogWarning(
                "InviteUser command missing required data. PayloadLength={PayloadLength}.",
                commandJson?.Length ?? 0);
            return;
        }

        logger.LogInformation(
            "Handling {MessageType} from queue {QueueName}",
            nameof(InviteUserCommand),
            QueueName);

        if (string.IsNullOrWhiteSpace(command.InvitationToken))
        {
            logger.LogWarning("InviteUser command is missing invitation token for UserId {UserId}.", command.UserId);
            return;
        }

        logger.LogInformation(
            "Received InviteUser command for UserId {UserId}, Email {MaskedEmail}",
            command.UserId,
            MaskEmail(command.Email));

        await inviteEmailSender.SendInviteAsync(command, ct);
    }

    private static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "<empty>";

        var trimmed = email.Trim();
        var atIndex = trimmed.IndexOf('@');
        if (atIndex <= 1 || atIndex == trimmed.Length - 1)
            return "***";

        return $"{trimmed[0]}***{trimmed[(atIndex - 1)]}{trimmed[atIndex..]}";
    }
}
