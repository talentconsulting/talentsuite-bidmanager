using TalentSuite.Shared.Messaging.Events;

namespace TalentSuite.Functions.CommentEmail;

public interface ICommentMentionEmailSender
{
    Task SendCommentMentionAsync(
        CommentSavedWithMentionsEvent @event,
        CommentMentionedUser mentionedUser,
        CancellationToken ct = default);
}
