namespace TalentSuite.Shared.Bids;

public enum RedReviewState
{
    Pending,
    ChangesRequested,
    Complete
}

public sealed class RedReviewReviewerResponse
{
    public string UserId { get; set; } = string.Empty;
    public RedReviewState State { get; set; } = RedReviewState.Pending;
}

public sealed class RedReviewResponse
{
    public string QuestionId { get; set; } = string.Empty;
    public string ResultText { get; set; } = string.Empty;
    public RedReviewState State { get; set; } = RedReviewState.Pending;
    public List<RedReviewReviewerResponse> Reviewers { get; set; } = new();
    public List<DraftCommentResponse> Comments { get; set; } = new();
}

public sealed class UpdateRedReviewRequest
{
    public string ResultText { get; set; } = string.Empty;
    public RedReviewState State { get; set; } = RedReviewState.Pending;
    public List<RedReviewReviewerResponse> Reviewers { get; set; } = new();
}
