namespace TalentSuite.Shared.Bids;

public sealed class UpdateBidOverviewRequest
{
    public string Company { get; set; } = string.Empty;
    public string? UniqueReference { get; set; }
    public string? Summary { get; set; }
    public string? KeyInformation { get; set; }
    public string? Budget { get; set; }
    public string? DeadlineForQualifying { get; set; }
    public string? DeadlineForSubmission { get; set; }
    public string? LengthOfContract { get; set; }
}
