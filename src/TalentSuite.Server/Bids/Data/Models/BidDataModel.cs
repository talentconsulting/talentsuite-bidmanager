using TalentSuite.Shared.Bids;

namespace TalentSuite.Server.Bids.Data.Models;

public class BidDataModel
{
    public BidDataModel() // for deserialisation
    {
    }
    
    public BidDataModel(string id): base()
    {
        Id = id;
    }
    
    public string Id { get; set; }

    public string? Company { get; set; }
    public string? Summary { get; set; }
    public string? KeyInformation { get; set; }
    public string? Budget { get; set; }
    public string? DeadlineForQualifying { get; set; }
    public string? DeadlineForSubmission { get; set; }
    public string? LengthOfContract { get; set; }
    public BidStage Stage { get; set; } = BidStage.Stage1;
    public BidStatus Status { get; set; } = BidStatus.Underway;
    public BidLibraryPushDataModel? BidLibraryPush { get; set; }
    public List<QuestionDataModel> Questions { get; set; } = new();
}
