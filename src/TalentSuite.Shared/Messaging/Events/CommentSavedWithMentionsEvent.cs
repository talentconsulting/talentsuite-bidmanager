namespace TalentSuite.Shared.Messaging.Events;

public sealed class CommentSavedWithMentionsEvent
{
    public string BidId { get; set; } = string.Empty;
    public string QuestionId { get; set; } = string.Empty;
    public string CommentId { get; set; } = string.Empty;
    public string Tab { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public string SelectedText { get; set; } = string.Empty;
    public string QuestionLink { get; set; } = string.Empty;
    public List<CommentMentionedUser> MentionedUsers { get; set; } = new();
}

public sealed class CommentMentionedUser
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
