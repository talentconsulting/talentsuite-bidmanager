namespace TalentSuite.Shared.Bids.List;

public class BidListItemResponse
{
    public string Id { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public int QuestionCount { get; set; }
    public string OwnerId { get; set; }
    public BidStatus Status { get; set; } = BidStatus.Underway;
}
