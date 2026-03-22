using TalentSuite.Shared.Bids;

namespace TalentSuite.Server.Bids.Services.Models;

public class CreateBidModel
{
    public string? Category { get; set; }
    public string? UniqueReference { get; set; }
    public string? Company { get; set; }
    public string? Summary { get; set; }
    public string? KeyInformation { get; set; }
    public string? Budget { get; set; }
    public string? DeadlineForQualifying { get; set; }
    public string? DeadlineForSubmission { get; set; }
    public string? LengthOfContract { get; set; }
    public BidStage Stage { get; set; } = BidStage.Stage1;
    public BidStatus Status { get; set; } = BidStatus.Underway;
    public List<CreateQuestionModel> Questions { get; set; } = new();
}
