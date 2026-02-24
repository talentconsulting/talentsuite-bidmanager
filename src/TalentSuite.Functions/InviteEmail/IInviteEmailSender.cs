using TalentSuite.Shared.Messaging.Commands;

namespace TalentSuite.Functions.InviteEmail;

public interface IInviteEmailSender
{
    Task SendInviteAsync(InviteUserCommand command, CancellationToken ct = default);
}
