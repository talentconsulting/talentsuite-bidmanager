using TalentSuite.Shared.Bids;

namespace TalentSuite.Shared.Tasks;

public sealed class AssignedQuestionTaskResponse
{
    public string BidId { get; set; } = string.Empty;
    public string BidTitle { get; set; } = string.Empty;
    public string QuestionId { get; set; } = string.Empty;
    public string QuestionTitle { get; set; } = string.Empty;
    public QuestionUserRole Role { get; set; } = QuestionUserRole.Reviewer;
}
