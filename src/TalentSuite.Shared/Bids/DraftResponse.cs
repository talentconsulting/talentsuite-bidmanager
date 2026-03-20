namespace TalentSuite.Shared.Bids;

public sealed class DraftResponse
{
    public string Response { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public List<DraftCommentResponse> Comments { get; set; } = new();
}
