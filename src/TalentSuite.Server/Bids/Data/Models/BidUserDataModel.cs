using TalentSuite.Shared.Users;

namespace TalentSuite.Server.Bids.Data.Models;

public class BidUserDataModel
{
    public string Id { get; set; }
    public string Name { get; set; }
    public UserRole Role { get; set; }
    public bool IsOwner { get; set; }
}