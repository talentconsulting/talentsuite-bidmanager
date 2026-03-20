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
            logger.LogError(ex, "Failed to deserialize InviteUser command payload: {Payload}", commandJson);
            throw;
        }

        if (command is null || string.IsNullOrWhiteSpace(command.UserId) || string.IsNullOrWhiteSpace(command.Email))
        {
            logger.LogWarning("InviteUser command missing required data. Payload: {Payload}", commandJson);
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
            "Received InviteUser command for UserId {UserId}, Email {Email}",
            command.UserId,
            command.Email);

        await inviteEmailSender.SendInviteAsync(command, ct);
    }
}
