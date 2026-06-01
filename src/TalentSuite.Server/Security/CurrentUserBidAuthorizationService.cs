using System.Security.Claims;
using TalentSuite.Server.Bids.Services;
using TalentSuite.Server.Users.Services;

namespace TalentSuite.Server.Security;

public interface ICurrentUserBidAuthorizationService
{
    bool IsAdmin(ClaimsPrincipal user);
    bool IsBidManager(ClaimsPrincipal user);
    bool CanManageAssignedBids(ClaimsPrincipal user);
    Task<string?> ResolveCurrentUserIdAsync(ClaimsPrincipal user, CancellationToken ct = default);
    Task<bool> HasBidAccessAsync(ClaimsPrincipal user, string bidId, CancellationToken ct = default);
    Task<bool> CanManageBidAsync(ClaimsPrincipal user, string bidId, CancellationToken ct = default);
}

public sealed class CurrentUserBidAuthorizationService(
    IBidService bidService,
    IUserService userService) : ICurrentUserBidAuthorizationService
{
    public bool IsAdmin(ClaimsPrincipal user)
        => user.IsInRole("admin") || user.IsInRole("Admin");

    public bool IsBidManager(ClaimsPrincipal user)
        => user.IsInRole("bidManager") || user.IsInRole("BidManager");

    public bool CanManageAssignedBids(ClaimsPrincipal user)
        => IsAdmin(user) || IsBidManager(user);

    public async Task<string?> ResolveCurrentUserIdAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        var subject = user.FindFirst("sub")?.Value
                      ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var username = user.FindFirst("preferred_username")?.Value
                       ?? user.FindFirst(ClaimTypes.Name)?.Value;

        var users = await userService.GetUsers(ct);

        if (!string.IsNullOrWhiteSpace(subject))
        {
            var bySubject = users.FirstOrDefault(u =>
                string.Equals(u.IdentitySubject, subject, StringComparison.Ordinal));
            if (bySubject is not null)
                return bySubject.Id;

            var byId = users.FirstOrDefault(u =>
                string.Equals(u.Id, subject, StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
                return byId.Id;
        }

        if (!string.IsNullOrWhiteSpace(username))
        {
            var byUsername = users.FirstOrDefault(u =>
                string.Equals(u.IdentityUsername, username, StringComparison.OrdinalIgnoreCase));
            if (byUsername is not null)
                return byUsername.Id;

            var byId = users.FirstOrDefault(u =>
                string.Equals(u.Id, username, StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
                return byId.Id;
        }

        return null;
    }

    public async Task<bool> HasBidAccessAsync(ClaimsPrincipal user, string bidId, CancellationToken ct = default)
    {
        if (IsAdmin(user))
            return true;

        if (string.IsNullOrWhiteSpace(bidId))
            return false;

        var userId = await ResolveCurrentUserIdAsync(user, ct);
        if (string.IsNullOrWhiteSpace(userId))
            return false;

        var bidUsers = await bidService.GetBidUsers(bidId, ct);
        return bidUsers.Any(x => string.Equals(x.Id, userId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> CanManageBidAsync(ClaimsPrincipal user, string bidId, CancellationToken ct = default)
    {
        if (!CanManageAssignedBids(user))
            return false;

        return await HasBidAccessAsync(user, bidId, ct);
    }
}
