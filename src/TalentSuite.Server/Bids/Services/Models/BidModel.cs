namespace TalentSuite.Server.Bids.Services.Models;

public class BidModel : CreateBidModel
{
    public Guid Id { get; set; }
    public BidLibraryPushModel? BidLibraryPush { get; set; }
}
