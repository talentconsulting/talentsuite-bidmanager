using TalentSuite.Shared.Bids;

namespace TalentSuite.Server.Bids.Data.Models;

public class SearchItemDataModel
{
    public string Id { get; set; }
    public string Company { get; set; }
    public string Summary { get; set; }
    public int QuestionCount { get; set; }
    public BidStatus Status { get; set; } = BidStatus.Underway;
}
