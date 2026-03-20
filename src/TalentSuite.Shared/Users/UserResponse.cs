namespace TalentSuite.Shared.Users;

public class UserResponse
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public UserRole Role { get; set; } = UserRole.User;
    public bool HasAcceptedRegistration { get; set; }
    public string IdentityProvider { get; set; } = string.Empty;
    public string IdentitySubject { get; set; } = string.Empty;
    public string IdentityUsername { get; set; } = string.Empty;
    public string InvitationToken { get; set; } = string.Empty;
    public DateTimeOffset? InvitationExpiresAtUtc { get; set; }
}

public class UserAssignmentRequest
{
    public string UserId  { get; set; }
}

public class UpdateUserRequest
{
    public string Name { get; set; }
    public string Email { get; set; }
    public UserRole Role { get; set; } = UserRole.User;
    public bool HasAcceptedRegistration { get; set; }
}

public class CreateUserRequest
{
    public string Name { get; set; }
    public string Email { get; set; }
    public UserRole Role { get; set; } = UserRole.User;
    public bool HasAcceptedRegistration { get; set; }
}

public class AcceptInviteRequest
{
    public string InvitationToken { get; set; } = string.Empty;
}

public class RegisterInviteRequest
{
    public string InvitationToken { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class ResendInviteResponse
{
    public string InvitationToken { get; set; } = string.Empty;
    public DateTimeOffset? InvitationExpiresAtUtc { get; set; }
}

public class CurrentUserProfileResponse
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.User;
    public bool HasAcceptedRegistration { get; set; }
    public string IdentityProvider { get; set; } = string.Empty;
    public string IdentitySubject { get; set; } = string.Empty;
    public string IdentityUsername { get; set; } = string.Empty;
}

public class CurrentUserAuthorisationResponse
{
    public bool IsAdmin { get; set; }
    public List<string> Roles { get; set; } = new();
}
