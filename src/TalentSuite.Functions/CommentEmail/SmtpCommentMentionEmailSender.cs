using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TalentSuite.Shared.Messaging.Events;

namespace TalentSuite.Functions.CommentEmail;

public sealed class SmtpCommentMentionEmailSender(
    IOptions<EmailOptions> options,
    ILogger<SmtpCommentMentionEmailSender> logger) : ICommentMentionEmailSender
{
    private readonly EmailOptions _options = options.Value;
    private const string TemplateRelativePath = "CommentEmail/templates/comment-mention-email.html";

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
        var textBody = BuildTextBody(@event, mentionedUser);
        var htmlBody = BuildHtmlBody(@event, mentionedUser);

        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromEmail, _options.FromDisplayName),
            Subject = subject,
            Body = textBody,
            IsBodyHtml = false
        };
        message.To.Add(mentionedUser.Email);
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

    private string BuildHtmlBody(CommentSavedWithMentionsEvent @event, CommentMentionedUser user)
    {
        var template = TryLoadHtmlTemplate();
        if (string.IsNullOrWhiteSpace(template))
        {
            logger.LogWarning(
                "Comment mention email HTML template not found at {TemplatePath}. Falling back to text email body.",
                TemplateRelativePath);
            return BuildTextBody(@event, user);
        }

        var greetingName = string.IsNullOrWhiteSpace(user.FullName) ? "there" : user.FullName;
        var encodedLink = WebUtility.HtmlEncode(@event.QuestionLink ?? string.Empty);
        var selectedText = @event.SelectedText ?? string.Empty;
        var selectedTextSection = string.IsNullOrWhiteSpace(selectedText)
            ? string.Empty
            : $"<p style=\"margin:0 0 18px 0;font-size:14px;line-height:1.6;color:#334155;\"><strong>Selected text:</strong><br/><span style=\"display:inline-block;margin-top:6px;padding:10px 12px;border-left:3px solid #cbd5e1;background:#f8fafc;\">{WebUtility.HtmlEncode(selectedText)}</span></p>";

        return template
            .Replace("{{GreetingName}}", WebUtility.HtmlEncode(greetingName), StringComparison.Ordinal)
            .Replace("{{Tab}}", WebUtility.HtmlEncode(@event.Tab ?? string.Empty), StringComparison.Ordinal)
            .Replace("{{Comment}}", WebUtility.HtmlEncode(@event.Comment ?? string.Empty), StringComparison.Ordinal)
            .Replace("{{SelectedTextSection}}", selectedTextSection, StringComparison.Ordinal)
            .Replace("{{QuestionLink}}", encodedLink, StringComparison.Ordinal)
            .Replace("{{CurrentYear}}", DateTime.UtcNow.Year.ToString(), StringComparison.Ordinal);
    }

    private static string BuildTextBody(CommentSavedWithMentionsEvent @event, CommentMentionedUser user)
    {
        var greetingName = string.IsNullOrWhiteSpace(user.FullName) ? "there" : user.FullName;
        return
            $"Hi {greetingName},{Environment.NewLine}{Environment.NewLine}" +
            "You have been asked to do some work for a bid question." + Environment.NewLine +
            $"Comment tab: {@event.Tab}{Environment.NewLine}" +
            BuildSelectedTextLine(@event.SelectedText) +
            $"Comment: {@event.Comment}{Environment.NewLine}{Environment.NewLine}" +
            "Open the question here:" + Environment.NewLine +
            $"{@event.QuestionLink}{Environment.NewLine}";
    }

    private static string BuildSelectedTextLine(string? selectedText)
    {
        if (string.IsNullOrWhiteSpace(selectedText))
            return string.Empty;

        return $"Selected text: \"{selectedText}\"{Environment.NewLine}";
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
