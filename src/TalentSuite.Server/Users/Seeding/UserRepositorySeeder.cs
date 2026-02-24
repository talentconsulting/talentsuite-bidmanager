using TalentSuite.Server.Users.Data;
using TalentSuite.Server.Users.Services.DataModels;
using TalentSuite.Shared.Users;

namespace TalentSuite.Server.Users.Seeding;

public sealed class UserRepositorySeeder
{
    private const string RichardSubject = "3e22f0c0-e221-49ba-a85b-483c22002e38";
    private const string RichardUsername = "richard.parkins";
    private const string KarenSubject = "a2b0eb66-75d7-40ad-8548-345533685f23";
    private const string KarenUsername = "karen.spearing";

    private readonly IManageUsers _users;
    private readonly ILogger<UserRepositorySeeder> _logger;

    public UserRepositorySeeder(IManageUsers users, ILogger<UserRepositorySeeder> logger)
    {
        _users = users;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var existingUsers = await _users.GetUsers(ct);

        await EnsureRichardSeededAsync(existingUsers, ct);
        await EnsureKarenSeededAsync(existingUsers, ct);
    }

    private async Task EnsureRichardSeededAsync(List<UserDataModel> users, CancellationToken ct)
    {
        var richard = users.FirstOrDefault(u =>
            string.Equals(u.IdentitySubject, RichardSubject, StringComparison.Ordinal) ||
            string.Equals(u.IdentityUsername, RichardUsername, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(u.Email, "rgparkins@hotmail.com", StringComparison.OrdinalIgnoreCase));

        if (richard is null)
        {
            var created = await _users.AddUser(new UserDataModel
            {
                Name = "Richard Parkins",
                Email = "rgparkins@hotmail.com",
                Role = UserRole.Admin
            }, ct);

            if (string.IsNullOrWhiteSpace(created.InvitationToken))
            {
                _logger.LogWarning("Could not seed Richard Parkins identity link: invitation token was empty.");
                return;
            }

            var accepted = await _users.AcceptInvite(
                created.InvitationToken,
                "keycloak",
                RichardSubject,
                RichardUsername,
                "rgparkins@hotmail.com",
                ct);

            if (accepted is null)
                _logger.LogWarning("Could not seed Richard Parkins identity link via AcceptInvite.");

            return;
        }

        if (string.Equals(richard.IdentitySubject, RichardSubject, StringComparison.Ordinal))
            return;

        if (string.IsNullOrWhiteSpace(richard.InvitationToken))
        {
            _logger.LogWarning(
                "Richard Parkins exists but is linked to a different identity subject ({IdentitySubject}) and has no invitation token for relinking.",
                richard.IdentitySubject);
            return;
        }

        var relinked = await _users.AcceptInvite(
            richard.InvitationToken,
            "keycloak",
            RichardSubject,
            RichardUsername,
            richard.Email,
            ct);

        if (relinked is null)
        {
            _logger.LogWarning(
                "Failed to relink Richard Parkins to seeded identity subject {IdentitySubject}.",
                RichardSubject);
        }
    }

    private async Task EnsureKarenSeededAsync(List<UserDataModel> users, CancellationToken ct)
    {
        var karen = users.FirstOrDefault(u =>
            string.Equals(u.IdentitySubject, KarenSubject, StringComparison.Ordinal) ||
            string.Equals(u.IdentityUsername, KarenUsername, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(u.Email, "karen.spearing@hotmail.com", StringComparison.OrdinalIgnoreCase));

        if (karen is null)
        {
            var created = await _users.AddUser(new UserDataModel
            {
                Name = "Karen Spearing",
                Email = "karen.spearing@hotmail.com",
                Role = UserRole.Admin
            }, ct);

            if (string.IsNullOrWhiteSpace(created.InvitationToken))
            {
                _logger.LogWarning("Could not seed Karen Spearing identity link: invitation token was empty.");
                return;
            }

            var accepted = await _users.AcceptInvite(
                created.InvitationToken,
                "keycloak",
                KarenSubject,
                KarenUsername,
                "karen.spearing@hotmail.com",
                ct);

            if (accepted is null)
                _logger.LogWarning("Could not seed Karen Spearing identity link via AcceptInvite.");

            return;
        }

        if (string.Equals(karen.IdentitySubject, KarenSubject, StringComparison.Ordinal))
            return;

        if (string.IsNullOrWhiteSpace(karen.InvitationToken))
        {
            _logger.LogWarning(
                "Karen Spearing exists but is linked to a different identity subject ({IdentitySubject}) and has no invitation token for relinking.",
                karen.IdentitySubject);
            return;
        }

        var relinked = await _users.AcceptInvite(
            karen.InvitationToken,
            "keycloak",
            KarenSubject,
            KarenUsername,
            karen.Email,
            ct);

        if (relinked is null)
        {
            _logger.LogWarning(
                "Failed to relink Karen Spearing to seeded identity subject {IdentitySubject}.",
                KarenSubject);
        }
    }
}
