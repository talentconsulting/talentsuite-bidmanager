namespace TalentSuite.Shared.Bids;

public sealed class CreateBidRequest
{
    public string? Company { get; set; }
    public string? Summary { get; set; }
    public string? KeyInformation { get; set; }
    public string? Budget { get; set; }
    public string? DeadlineForQualifying { get; set; }
    public string? DeadlineForSubmission { get; set; }
    public string? LengthOfContract { get; set; }
    public BidStage Stage { get; set; } = BidStage.Stage1;
    public BidStatus Status { get; set; } = BidStatus.Underway;
    public List<CreateQuestionRequest> Questions { get; set; } = new();
}
