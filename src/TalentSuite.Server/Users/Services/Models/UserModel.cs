using TalentSuite.Shared.Users;

namespace TalentSuite.Server.Users.Services.Models;

public class UserModel
{
    public string Id { get; set; }
    public string Name  { get; set; }
    public string Email { get; set; }
    public UserRole Role { get; set; }
    public bool HasAcceptedRegistration { get; set; }
    public string IdentityProvider { get; set; } = string.Empty;
    public string IdentitySubject { get; set; } = string.Empty;
    public string IdentityUsername { get; set; } = string.Empty;
    public string InvitationToken { get; set; } = string.Empty;
    public DateTimeOffset? InvitationExpiresAtUtc { get; set; }
}
