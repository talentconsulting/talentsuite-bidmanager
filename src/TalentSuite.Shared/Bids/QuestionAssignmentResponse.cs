namespace TalentSuite.Shared.Bids;

public enum QuestionUserRole
{
    Owner,
    Reviewer,
    Support
}

public sealed class QuestionAssignmentResponse
{
    public string UserId { get; set; } = string.Empty;
    public QuestionUserRole Role { get; set; } = QuestionUserRole.Reviewer;
}

public sealed class QuestionUserAssignmentRequest
{
    public string UserId { get; set; } = string.Empty;
    public QuestionUserRole Role { get; set; } = QuestionUserRole.Reviewer;
}
