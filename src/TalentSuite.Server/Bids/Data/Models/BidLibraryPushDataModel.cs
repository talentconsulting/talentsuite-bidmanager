namespace TalentSuite.Server.Bids.Data.Models;

public sealed class BidLibraryPushDataModel
{
    public string BidId { get; set; } = string.Empty;
    public string PerformedByUserId { get; set; } = string.Empty;
    public string PerformedByName { get; set; } = string.Empty;
    public DateTime PushedAtUtc { get; set; }
}
