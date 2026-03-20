using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TalentSuite.Shared.Messaging.Commands;

namespace TalentSuite.Functions.InviteEmail;

public sealed class SmtpInviteEmailSender(
    IOptions<EmailOptions> options,
    ILogger<SmtpInviteEmailSender> logger) : IInviteEmailSender
{
    private readonly EmailOptions _options = options.Value;
    private const string TemplateRelativePath = "InviteEmail/templates/invite-email.html";

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
        var textBody = BuildTextBody(inviteUrl);
        var htmlBody = BuildHtmlBody(inviteUrl);

        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromEmail, _options.FromDisplayName),
            Subject = subject,
            Body = textBody,
            IsBodyHtml = false
        };
        message.To.Add(command.Email);
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
            textBody,
            Encoding.UTF8,
            MediaTypeNames.Text.Plain));
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
            htmlBody,
            Encoding.UTF8,
            MediaTypeNames.Text.Html));

        using var client = new SmtpClient(_options.SmtpHost, _options.SmtpPort)
        {
            EnableSsl = _options.SmtpEnableSsl,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(_options.SmtpUsername, _options.SmtpPassword)
        };

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

    private string BuildHtmlBody(string inviteUrl)
    {
        var template = TryLoadHtmlTemplate();
        if (string.IsNullOrWhiteSpace(template))
        {
            logger.LogWarning(
                "Invite email HTML template not found at {TemplatePath}. Falling back to text email.",
                TemplateRelativePath);
            return BuildTextBody(inviteUrl);
        }

        var encodedUrl = WebUtility.HtmlEncode(inviteUrl);
        return template
            .Replace("{{InviteUrl}}", encodedUrl, StringComparison.Ordinal)
            .Replace("{{CurrentYear}}", DateTime.UtcNow.Year.ToString(), StringComparison.Ordinal);
    }

    private static string BuildTextBody(string inviteUrl)
    {
        return
            $"You have been invited to TalentSuite.{Environment.NewLine}{Environment.NewLine}" +
            $"To accept your invite and create your account, open:{Environment.NewLine}" +
            $"{inviteUrl}{Environment.NewLine}{Environment.NewLine}" +
            "If you did not expect this invitation, you can ignore this email.";
    }

    private static string? TryLoadHtmlTemplate()
    {
        var candidatePaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, TemplateRelativePath),
            Path.Combine(Directory.GetCurrentDirectory(), TemplateRelativePath)
        };

        foreach (var path in candidatePaths)
        {
            if (!File.Exists(path))
                continue;

            return File.ReadAllText(path, Encoding.UTF8);
        }

        return null;
    }
}
