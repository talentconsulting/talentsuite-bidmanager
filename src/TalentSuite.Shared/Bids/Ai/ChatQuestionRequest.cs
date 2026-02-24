namespace TalentSuite.Shared.Bids.Ai;

public class ChatQuestionRequest
{
    public string BidId { get; set; } = string.Empty;
    public string QuestionId { get; set; } = string.Empty;
    public string FreeTextQuestion { get; set; } = string.Empty;
    public string? ThreadId { get; set; }
}
