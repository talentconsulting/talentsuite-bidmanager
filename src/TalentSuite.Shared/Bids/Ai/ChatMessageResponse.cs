namespace TalentSuite.Shared.Bids.Ai;

public class ChatMessageResponse
{
    public string Id { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}
