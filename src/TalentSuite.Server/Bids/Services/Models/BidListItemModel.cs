using TalentSuite.Shared.Bids;

namespace TalentSuite.Server.Bids.Services.Models;

public class BidListItemModel
{
    public string Id { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public int QuestionCount { get; set; }
    public BidStatus Status { get; set; } = BidStatus.Underway;
}
