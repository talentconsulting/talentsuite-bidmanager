using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TalentSuite.Shared.Messaging.Commands;

namespace TalentSuite.Functions.InviteEmail;

public sealed class SmtpInviteEmailSender(
    IOptions<EmailOptions> options,
    ILogger<SmtpInviteEmailSender> logger) : IInviteEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public async Task SendInviteAsync(InviteUserCommand command, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation(
                "Invite email sending is disabled. Skipping send for UserId {UserId}.",
                command.UserId);
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.SmtpHost) ||
            string.IsNullOrWhiteSpace(_options.FromEmail))
        {
            logger.LogWarning(
                "Invite email settings are incomplete (SmtpHost/FromEmail). Skipping send for UserId {UserId}.",
                command.UserId);
            return;
        }

        var inviteUrl = BuildInviteUrl(command.InvitationToken);
        var subject = "You're invited to TalentSuite";
        var body = BuildBody(inviteUrl);

        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromEmail, _options.FromDisplayName),
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };
        message.To.Add(command.Email);

        using var client = new SmtpClient(_options.SmtpHost, _options.SmtpPort)
        {
            EnableSsl = _options.SmtpEnableSsl
        };

        if (!string.IsNullOrWhiteSpace(_options.SmtpUsername))
        {
            client.Credentials = new NetworkCredential(_options.SmtpUsername, _options.SmtpPassword);
        }

        ct.ThrowIfCancellationRequested();
        await client.SendMailAsync(message);

        logger.LogInformation(
            "Invite email sent to {Email} for UserId {UserId}.",
            command.Email,
            command.UserId);
    }

    private string BuildInviteUrl(string invitationToken)
    {
        var baseUrl = (_options.FrontendBaseUrl ?? string.Empty).TrimEnd('/');
        var token = Uri.EscapeDataString(invitationToken ?? string.Empty);
        return $"{baseUrl}/accept-invite?token={token}";
    }

    private static string BuildBody(string inviteUrl)
    {
        return
            $"You have been invited to TalentSuite.{Environment.NewLine}{Environment.NewLine}" +
            $"To accept your invite and create your account, open:{Environment.NewLine}" +
            $"{inviteUrl}{Environment.NewLine}{Environment.NewLine}" +
            "If you did not expect this invitation, you can ignore this email.";
    }
}
