namespace TalentSuite.Shared.Bids;

public sealed class DraftCommentResponse
{
    public string Id { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public bool IsComplete { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public int? StartIndex { get; set; }
    public int? EndIndex { get; set; }
    public string SelectedText { get; set; } = string.Empty;
}
