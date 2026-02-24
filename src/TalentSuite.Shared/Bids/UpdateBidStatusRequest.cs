namespace TalentSuite.Shared.Bids;

public sealed class UpdateBidStatusRequest
{
    public BidStatus Status { get; set; } = BidStatus.Underway;
}
