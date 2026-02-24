namespace TalentSuite.Server.Bids.Data.Models;

public sealed class FinalAnswerDataModel
{
    public string QuestionId { get; set; } = string.Empty;
    public string AnswerText { get; set; } = string.Empty;
    public bool ReadyForSubmission { get; set; }
    public List<DraftCommentDataModel> Comments { get; set; } = new();
}
