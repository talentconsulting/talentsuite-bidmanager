namespace TalentSuite.Shared.Bids;

public sealed class AddDraftCommentRequest
{
    public string Comment { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public List<string> MentionedUserIds { get; set; } = new();
    public int? StartIndex { get; set; }
    public int? EndIndex { get; set; }
    public string SelectedText { get; set; } = string.Empty;
}
