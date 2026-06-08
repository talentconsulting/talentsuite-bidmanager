using TalentSuite.Shared.Users;

namespace TalentSuite.Server.Users.Services.DataModels;

public class UserDataModel
{
    public UserDataModel()
    {
        
    }

    public UserDataModel(string id)
    {
        Id = id;
    }

    public string Id { get; set; } = string.Empty;
    public string Name  { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public bool HasAcceptedRegistration { get; set; }
    public string IdentityProvider { get; set; } = string.Empty;
    public string IdentitySubject { get; set; } = string.Empty;
    public string IdentityUsername { get; set; } = string.Empty;
    public string InvitationToken { get; set; } = string.Empty;
    public DateTimeOffset? InvitationExpiresAtUtc { get; set; }
}
