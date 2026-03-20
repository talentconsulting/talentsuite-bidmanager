namespace TalentSuite.Shared.Bids;

public sealed class FinalAnswerResponse
{
    public string QuestionId { get; set; } = string.Empty;
    public string AnswerText { get; set; } = string.Empty;
    public bool ReadyForSubmission { get; set; }
    public List<DraftCommentResponse> Comments { get; set; } = new();
}

public sealed class UpdateFinalAnswerRequest
{
    public string AnswerText { get; set; } = string.Empty;
    public bool ReadyForSubmission { get; set; }
}
