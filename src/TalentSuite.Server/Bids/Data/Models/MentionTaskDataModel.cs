namespace TalentSuite.Server.Bids.Data.Models;

public sealed class MentionTaskDataModel
{
    public string Id { get; set; } = string.Empty;
    public string BidId { get; set; } = string.Empty;
    public string QuestionId { get; set; } = string.Empty;
    public string CommentId { get; set; } = string.Empty;
    public string AssignedUserId { get; set; } = string.Empty;
    public string CommentText { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}
