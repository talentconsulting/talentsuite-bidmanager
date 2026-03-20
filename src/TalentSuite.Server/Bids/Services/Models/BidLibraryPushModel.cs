namespace TalentSuite.Server.Bids.Services.Models;

public sealed class BidLibraryPushModel
{
    public string BidId { get; set; } = string.Empty;
    public string PerformedByUserId { get; set; } = string.Empty;
    public string PerformedByName { get; set; } = string.Empty;
    public DateTime PushedAtUtc { get; set; }
}
