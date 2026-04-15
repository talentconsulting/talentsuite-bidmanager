namespace TalentSuite.Server.Users.Services;

public interface IKeycloakAdminService
{
    Task<bool> DeleteUserAsync(string? userId, string? username, string? email, CancellationToken ct = default);
    Task<string?> CreateUserAsync(string username, string email, string? name, string password, string role, CancellationToken ct = default);
    Task<bool> SyncRealmRoleAsync(string? userId, string? username, string? email, string role, CancellationToken ct = default);
}
