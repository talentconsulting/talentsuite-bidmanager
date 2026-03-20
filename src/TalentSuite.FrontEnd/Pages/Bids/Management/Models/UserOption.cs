using TalentSuite.Shared.Bids;

namespace TalentSuite.FrontEnd.Pages.Bids.Management.Models;

public sealed class UserOption
{
    public UserOption(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public string Id { get; }
    public string Name { get; }
}

public sealed class QuestionAssignedUserOption
{
    public QuestionAssignedUserOption(string id, string name, QuestionUserRole role)
    {
        Id = id;
        Name = name;
        Role = role;
    }

    public string Id { get; }
    public string Name { get; }
    public QuestionUserRole Role { get; }
}

public sealed class RedReviewReviewerOption
{
    public RedReviewReviewerOption(string userId, string name, RedReviewState state)
    {
        UserId = userId;
        Name = name;
        State = state;
    }

    public string UserId { get; }
    public string Name { get; }
    public RedReviewState State { get; }
}
