using TalentSuite.Shared.Users;

namespace TalentSuite.Server.Bids.Data.Models;

public class BidUserDataModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public bool IsOwner { get; set; }
}