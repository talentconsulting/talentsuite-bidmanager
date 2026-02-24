using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TalentSuite.Shared.Messaging.Events;

namespace TalentSuite.Functions.CommentEmail;

public sealed class SmtpCommentMentionEmailSender(
    IOptions<EmailOptions> options,
    ILogger<SmtpCommentMentionEmailSender> logger) : ICommentMentionEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public async Task SendCommentMentionAsync(
        CommentSavedWithMentionsEvent @event,
        CommentMentionedUser mentionedUser,
        CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation(
                "Mention email sending is disabled. Skipping send for {Email}.",
                mentionedUser.Email);
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.SmtpHost) ||
            string.IsNullOrWhiteSpace(_options.FromEmail))
        {
            logger.LogWarning(
                "Mention email settings are incomplete (SmtpHost/FromEmail). Skipping send for {Email}.",
                mentionedUser.Email);
            return;
        }

        if (string.IsNullOrWhiteSpace(mentionedUser.Email))
        {
            logger.LogWarning("Mentioned user email is empty. Skipping mention email send.");
            return;
        }

        var subject = "You have been asked to do work on a bid question";
        var body = BuildBody(@event, mentionedUser);

        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromEmail, _options.FromDisplayName),
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };
        message.To.Add(mentionedUser.Email);

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
            "Mention email sent to {Email} for BidId {BidId}, QuestionId {QuestionId}.",
            mentionedUser.Email,
            @event.BidId,
            @event.QuestionId);
    }

    private static string BuildBody(CommentSavedWithMentionsEvent @event, CommentMentionedUser user)
    {
        var greetingName = string.IsNullOrWhiteSpace(user.FullName) ? "there" : user.FullName;
        return
            $"Hi {greetingName},{Environment.NewLine}{Environment.NewLine}" +
            "You have been asked to do some work for a bid question." + Environment.NewLine +
            $"Comment tab: {@event.Tab}{Environment.NewLine}" +
            $"Comment: {@event.Comment}{Environment.NewLine}{Environment.NewLine}" +
            "Open the question here:" + Environment.NewLine +
            $"{@event.QuestionLink}{Environment.NewLine}";
    }
}
