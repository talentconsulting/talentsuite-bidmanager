using TalentSuite.Shared.Bids;

namespace TalentSuite.Server.Bids.Data.Models;

public class QuestionAssignmentDataModel
{
    public string UserId { get; set; } = string.Empty;
    public QuestionUserRole Role { get; set; } = QuestionUserRole.Reviewer;
}
