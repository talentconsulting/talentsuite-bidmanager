namespace TalentSuite.Server.Bids.Data.Models;

public class ChatMessageDataModel
{
    public string Id { get; set; } = string.Empty;
    public string BidId { get; set; } = string.Empty;
    public string QuestionId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}
