using TalentSuite.Server.Users.Services.DataModels;

namespace TalentSuite.Server.Users.Data;

public interface IManageUsers
{
    public Task<List<UserDataModel>> GetUsers(CancellationToken ct = default);
    public Task<UserDataModel?> GetUser(string userId, CancellationToken ct = default);
    public Task<UserDataModel> AddUser(UserDataModel user, CancellationToken ct = default);
    public Task<bool> UpdateUser(string userId, UserDataModel updatedUser, CancellationToken ct = default);
    public Task<bool> DeleteUser(string userId, CancellationToken ct = default);
    public Task<UserDataModel?> ResendInvite(string userId, CancellationToken ct = default);
    public Task<UserDataModel?> AcceptInvite(
        string invitationToken,
        string identityProvider,
        string identitySubject,
        string identityUsername,
        string email,
        CancellationToken ct = default);
}
