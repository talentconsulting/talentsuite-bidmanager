namespace TalentSuite.Server.Bids.Data.Models;

public class DraftCommentDataModel
{
    public DraftCommentDataModel(string id)
    {
        Id = id;
    }

    private DraftCommentDataModel() // for deserialisation
    {
    }

    public string Id { get; set; }
    public string Comment { get; set; } = string.Empty;
    public bool IsComplete { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public int? StartIndex { get; set; }
    public int? EndIndex { get; set; }
    public string SelectedText { get; set; } = string.Empty;
}
