using TalentSuite.Shared.Bids;

namespace TalentSuite.Shared.Messaging.Events;

public sealed class BidSubmittedEvent
{
    public string BidId { get; set; } = string.Empty;
    public BidResponse Bid { get; set; } = new();
    public Dictionary<string, string> FinalAnswerTextByQuestionId { get; set; } = new();
}
