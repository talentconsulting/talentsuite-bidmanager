using TalentSuite.Shared.Bids;

namespace TalentSuite.FrontEnd.Pages.Bids.Management.Models;

public sealed class BidManageModel
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Company { get; set; } = "";
    public string? Summary { get; set; }
    public string? KeyInformation { get; set; }
    public string? Budget { get; set; }
    public string? DeadlineForSubmission { get; set; }
    public string? DeadlineForQualifying { get; set; }
    public string? LengthOfContract { get; set; }
    public BidStage Stage { get; set; } = BidStage.Stage1;
    public BidStatus Status { get; set; } = BidStatus.Underway;
    public BidLibraryPushResponse? BidLibraryPush { get; set; }
    public List<BidQuestionModel> Questions { get; set; } = new();
}

public sealed class BidQuestionModel
{
    public string Id { get; set; } = "";
    public int QuestionOrderIndex { get; set; }
    public string Number { get; set; } = "";
    public string? Category { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Length { get; set; } = "";
    public int Weighting { get; set; }
    public bool Required { get; set; }
    public bool NiceToHave { get; set; }

    public List<QuestionAssignmentResponse> QuestionAssignments { get; set; } = new();
    public string ChatResponse { get; set; } = string.Empty;
    public string? ChatThreadId { get; set; }
    public string? FinalAnswer { get; set; }
    public bool ReadyForSubmission { get; set; }
    public List<DraftCommentResponse> FinalAnswerComments { get; set; } = new();

    public string? RedReviewAnswer { get; set; }
    public RedReviewState RedReviewState { get; set; } = RedReviewState.Pending;
    public List<RedReviewReviewerResponse> RedReviewReviewers { get; set; } = new();
    public List<DraftCommentResponse> RedReviewComments { get; set; } = new();
    public bool IsRedReviewLoaded { get; set; }
    public List<DraftResponse> DraftResponses { get; set; } = new();
}
