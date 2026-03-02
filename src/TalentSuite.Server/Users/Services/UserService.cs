using TalentSuite.Server.Users.Data;
using TalentSuite.Server.Users.Mappers;
using TalentSuite.Server.Users.Services.Models;
using TalentSuite.Shared.Messaging;
using TalentSuite.Shared.Messaging.Commands;

namespace TalentSuite.Server.Users.Services;

public interface IUserService
{
    Task<List<UserModel>> GetUsers(CancellationToken ct = default);
    Task<UserModel?> GetUser(string userId, CancellationToken ct = default);
    Task<UserModel> AddUser(UserModel request, CancellationToken ct = default);
    Task<bool> UpdateUser(string userId, UserModel request, CancellationToken ct = default);
    Task<bool> DeleteUser(string userId, CancellationToken ct = default);
    Task<UserModel?> ResendInvite(string userId, CancellationToken ct = default);
    Task<UserModel?> RegisterInvitedUser(string invitationToken, string username, string password, CancellationToken ct = default);
    Task<UserModel?> AcceptInvite(
        string invitationToken,
        string identityProvider,
        string identitySubject,
        string identityUsername,
        string email,
        CancellationToken ct = default);
}

public class UserService : IUserService
{
    private readonly IManageUsers _userRepository;
    private readonly UserMapper _mapper;
    private readonly IKeycloakAdminService _keycloakAdminService;
    private readonly IAzureServiceBusClient _azureServiceBusClient;
    private readonly ILogger<UserService> _logger;
    private readonly string _inviteUserEntityName;

    public UserService(
        IManageUsers usersRepository,
        UserMapper mapper,
        IKeycloakAdminService keycloakAdminService,
        IAzureServiceBusClient azureServiceBusClient,
        IConfiguration configuration,
        ILogger<UserService> logger)
    {
        _userRepository = usersRepository;
        _mapper = mapper;
        _keycloakAdminService = keycloakAdminService;
        _azureServiceBusClient = azureServiceBusClient;
        _inviteUserEntityName = configuration["AzureServiceBus:InviteUserEntityName"] ?? "invite-user";
        _logger = logger;
    }
    public async Task<List<UserModel>> GetUsers(CancellationToken ct = default)
    {
        var users = await _userRepository.GetUsers(ct);

        return _mapper.ToModels(users);
    }

    public async Task<UserModel?> GetUser(string userId, CancellationToken ct = default)
    {
        var user = await _userRepository.GetUser(userId, ct);
        if (user is null)
            return null;

        return _mapper.ToModel(user);
    }

    public async Task<UserModel> AddUser(UserModel request, CancellationToken ct = default)
    {
        var user = _mapper.ToDataModel(request);
        var created = await _userRepository.AddUser(user, ct);
        await _azureServiceBusClient.PublishAsync(
            _inviteUserEntityName,
            new InviteUserCommand
            {
                UserId = created.Id,
                Email = created.Email,
                InvitationToken = created.InvitationToken
            },
            ct);
        return _mapper.ToModel(created);
    }

    public async Task<bool> UpdateUser(string userId, UserModel request, CancellationToken ct = default)
    {
        var user = _mapper.ToDataModel(request);
        return await _userRepository.UpdateUser(userId, user, ct);
    }

    public async Task<bool> DeleteUser(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return false;

        var user = await _userRepository.GetUser(userId, ct);
        if (user is null)
            return false;

        if (string.Equals(user.IdentityProvider, "keycloak", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(user.IdentitySubject))
        {
            var keycloakDeleted = await _keycloakAdminService.DeleteUserAsync(
                user.IdentitySubject,
                null,
                null,
                ct);
            if (!keycloakDeleted)
            {
                _logger.LogWarning("User {UserId} was not deleted locally because Keycloak deletion failed.", userId);
                return false;
            }
        }

        return await _userRepository.DeleteUser(userId, ct);
    }

    public async Task<UserModel?> ResendInvite(string userId, CancellationToken ct = default)
    {
        var user = await _userRepository.ResendInvite(userId, ct);
        return user is null ? null : _mapper.ToModel(user);
    }

    public async Task<UserModel?> RegisterInvitedUser(string invitationToken, string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(invitationToken)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password))
        {
            _logger.LogWarning(
                "RegisterInvitedUser rejected due to missing input. tokenPresent={TokenPresent}, usernamePresent={UsernamePresent}, passwordPresent={PasswordPresent}",
                !string.IsNullOrWhiteSpace(invitationToken),
                !string.IsNullOrWhiteSpace(username),
                !string.IsNullOrWhiteSpace(password));
            return null;
        }

        var users = await _userRepository.GetUsers(ct);
        var pending = users.FirstOrDefault(u =>
            string.Equals(u.InvitationToken, invitationToken, StringComparison.Ordinal) &&
            u.InvitationExpiresAtUtc.HasValue &&
            u.InvitationExpiresAtUtc.Value >= DateTimeOffset.UtcNow);

        if (pending is null)
        {
            var sameTokenAnyState = users.FirstOrDefault(u =>
                string.Equals(u.InvitationToken, invitationToken, StringComparison.Ordinal));

            if (sameTokenAnyState is null)
            {
                _logger.LogWarning(
                    "RegisterInvitedUser failed: no user found for token prefix {TokenPrefix}.",
                    SafeTokenPrefix(invitationToken));
            }
            else if (!sameTokenAnyState.InvitationExpiresAtUtc.HasValue)
            {
                _logger.LogWarning(
                    "RegisterInvitedUser failed: token prefix {TokenPrefix} matched user {UserId} but expiry is missing.",
                    SafeTokenPrefix(invitationToken),
                    sameTokenAnyState.Id);
            }
            else
            {
                _logger.LogWarning(
                    "RegisterInvitedUser failed: token prefix {TokenPrefix} matched user {UserId} but expired at {ExpiresAtUtc}.",
                    SafeTokenPrefix(invitationToken),
                    sameTokenAnyState.Id,
                    sameTokenAnyState.InvitationExpiresAtUtc);
            }
            return null;
        }

        var keycloakSubject = await _keycloakAdminService.CreateUserAsync(
            username.Trim(),
            pending.Email ?? string.Empty,
            pending.Name,
            password,
            "user",
            ct);
        if (string.IsNullOrWhiteSpace(keycloakSubject))
        {
            _logger.LogWarning(
                "RegisterInvitedUser failed: Keycloak user creation returned empty subject for pending user {UserId} (token prefix {TokenPrefix}).",
                pending.Id,
                SafeTokenPrefix(invitationToken));
            return null;
        }

        var linked = await _userRepository.AcceptInvite(
            invitationToken,
            "keycloak",
            keycloakSubject,
            username.Trim(),
            pending.Email ?? string.Empty,
            ct);

        if (linked is null)
        {
            _logger.LogWarning(
                "RegisterInvitedUser failed: repository link step returned null for user {UserId} (token prefix {TokenPrefix}).",
                pending.Id,
                SafeTokenPrefix(invitationToken));
        }

        return linked is null ? null : _mapper.ToModel(linked);
    }

    public async Task<UserModel?> AcceptInvite(
        string invitationToken,
        string identityProvider,
        string identitySubject,
        string identityUsername,
        string email,
        CancellationToken ct = default)
    {
        var user = await _userRepository.AcceptInvite(
            invitationToken,
            identityProvider,
            identitySubject,
            identityUsername,
            email,
            ct);
        return user is null ? null : _mapper.ToModel(user);
    }

    private static string SafeTokenPrefix(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return "(empty)";

        var trimmed = token.Trim();
        return trimmed.Length <= 8 ? trimmed : trimmed[..8];
    }
}
