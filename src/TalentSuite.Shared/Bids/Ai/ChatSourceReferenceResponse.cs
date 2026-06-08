namespace TalentSuite.Shared.Bids.Ai;

public sealed class ChatSourceReferenceResponse
{
    public string Kind { get; set; } = string.Empty;
    public string? FileId { get; set; }
    public string? FileName { get; set; }
    public string? Uri { get; set; }
    public string? Title { get; set; }
    public string? Quote { get; set; }
    public bool IsFromBidLibrary { get; set; }
}
