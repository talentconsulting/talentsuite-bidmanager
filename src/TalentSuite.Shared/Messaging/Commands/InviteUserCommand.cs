namespace TalentSuite.Shared.Messaging.Commands;

public sealed class InviteUserCommand
{
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string InvitationToken { get; set; } = string.Empty;
}
