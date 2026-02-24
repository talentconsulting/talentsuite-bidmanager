using TalentSuite.Server.Users.Services.DataModels;
using TalentSuite.Shared.Users;

namespace TalentSuite.Server.Users.Data;

public class InMemoryUserRepository : IManageUsers
{
    private readonly Dictionary<string, UserDataModel> _users = new();
    
    public InMemoryUserRepository()
    {
        var id = "04d3fde7-8b47-4558-905b-1888fb8a4db0";
        
        _users.Add(id, new UserDataModel(id)
        {
            Name = "Richard Parkins",
            Email = "rgparkins@hotmail.com",
            Role = UserRole.Admin,
            HasAcceptedRegistration = true,
            IdentityProvider = "keycloak",
            IdentitySubject = "3e22f0c0-e221-49ba-a85b-483c22002e38",
            IdentityUsername = "richard.parkins"
        });

        id = "0cf878f8-0840-4e1f-81af-983462b73722";
        
        _users.Add(id, new UserDataModel(id)
        {
            Name = "Karen Spearing",
            Email = "karen.spearing@hotmail.com",
            Role = UserRole.Admin,
            HasAcceptedRegistration = true,
            IdentityProvider = "keycloak",
            IdentitySubject = "a2b0eb66-75d7-40ad-8548-345533685f23",
            IdentityUsername = "karen.spearing"
        });
    }

    public Task<List<UserDataModel>> GetUsers(CancellationToken ct = default)
    {
        return Task.FromResult(_users.Values.ToList());
    }

    public Task<UserDataModel?> GetUser(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return Task.FromResult<UserDataModel?>(null);

        _users.TryGetValue(userId, out var user);
        return Task.FromResult(user);
    }

    public Task<UserDataModel> AddUser(UserDataModel user, CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString();
        var stored = new UserDataModel(id)
        {
            Name = user.Name,
            Email = user.Email,
            Role = user.Role,
            HasAcceptedRegistration = false,
            InvitationToken = GenerateInvitationToken(),
            InvitationExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(7)
        };

        _users[id] = stored;
        return Task.FromResult(stored);
    }

    public Task<bool> UpdateUser(string userId, UserDataModel updatedUser, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || updatedUser is null)
            return Task.FromResult(false);

        if (!_users.ContainsKey(userId))
            return Task.FromResult(false);

        var existing = _users[userId];

        _users[userId] = new UserDataModel(userId)
        {
            Name = updatedUser.Name,
            Email = updatedUser.Email,
            Role = updatedUser.Role,
            HasAcceptedRegistration = updatedUser.HasAcceptedRegistration,
            IdentityProvider = existing.IdentityProvider,
            IdentitySubject = existing.IdentitySubject,
            IdentityUsername = existing.IdentityUsername,
            InvitationToken = existing.InvitationToken,
            InvitationExpiresAtUtc = existing.InvitationExpiresAtUtc
        };

        return Task.FromResult(true);
    }

    public Task<bool> DeleteUser(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return Task.FromResult(false);

        return Task.FromResult(_users.Remove(userId));
    }

    public Task<UserDataModel?> ResendInvite(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return Task.FromResult<UserDataModel?>(null);

        if (!_users.TryGetValue(userId, out var user))
            return Task.FromResult<UserDataModel?>(null);

        if (user.HasAcceptedRegistration)
            return Task.FromResult<UserDataModel?>(null);

        user.InvitationToken = GenerateInvitationToken();
        user.InvitationExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(7);

        return Task.FromResult<UserDataModel?>(user);
    }

    public Task<UserDataModel?> AcceptInvite(
        string invitationToken,
        string identityProvider,
        string identitySubject,
        string identityUsername,
        string email,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(invitationToken) || string.IsNullOrWhiteSpace(identitySubject))
            return Task.FromResult<UserDataModel?>(null);

        var matched = _users.Values.FirstOrDefault(u =>
            string.Equals(u.InvitationToken, invitationToken, StringComparison.Ordinal) &&
            u.InvitationExpiresAtUtc.HasValue &&
            u.InvitationExpiresAtUtc.Value >= DateTimeOffset.UtcNow);

        if (matched is null)
            return Task.FromResult<UserDataModel?>(null);

        var existingBySubject = _users.Values.FirstOrDefault(u =>
            !string.IsNullOrWhiteSpace(u.IdentitySubject) &&
            string.Equals(u.IdentitySubject, identitySubject, StringComparison.Ordinal));

        if (existingBySubject is not null && !string.Equals(existingBySubject.Id, matched.Id, StringComparison.Ordinal))
            return Task.FromResult<UserDataModel?>(null);

        matched.IdentityProvider = identityProvider ?? "keycloak";
        matched.IdentitySubject = identitySubject;
        matched.IdentityUsername = identityUsername ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(email))
            matched.Email = email;
        matched.HasAcceptedRegistration = true;
        matched.InvitationToken = string.Empty;
        matched.InvitationExpiresAtUtc = null;

        return Task.FromResult<UserDataModel?>(matched);
    }

    private static string GenerateInvitationToken() => Guid.NewGuid().ToString("N");
}
