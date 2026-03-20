using TalentSuite.Shared.Bids;

namespace TalentSuite.Server.Bids.Data.Models;

public sealed class RedReviewReviewerDataModel
{
    public string UserId { get; set; } = string.Empty;
    public RedReviewState State { get; set; } = RedReviewState.Pending;
}

public sealed class RedReviewDataModel
{
    public string QuestionId { get; set; } = string.Empty;
    public string ResultText { get; set; } = string.Empty;
    public RedReviewState State { get; set; } = RedReviewState.Pending;
    public List<RedReviewReviewerDataModel> Reviewers { get; set; } = new();
    public List<DraftCommentDataModel> Comments { get; set; } = new();
}
