namespace TalentSuite.Shared.Bids.Ai;

public class ChatStreamUpdate
{
    public string Type { get; set; } = string.Empty;
    public string? Content { get; set; }
    public string? ThreadId { get; set; }
    public string? Error { get; set; }
}
