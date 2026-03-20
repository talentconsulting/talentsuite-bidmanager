using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using TalentSuite.Server.Bids.Services;
using TalentSuite.Server.Users.Services;

namespace TalentSuite.Server.Security;

public sealed class BidAccessAuthorizationHandler(
    IBidService bidService,
    IUserService userService) : AuthorizationHandler<BidAccessRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        BidAccessRequirement requirement)
    {
        if (!context.User.Identity?.IsAuthenticated ?? true)
            return;

        if (context.User.IsInRole("admin") || context.User.IsInRole("Admin"))
        {
            context.Succeed(requirement);
            return;
        }

        var bidId = TryGetBidId(context.Resource);
        if (string.IsNullOrWhiteSpace(bidId))
            return;

        var userId = await ResolveCurrentUserIdAsync(context.User);
        if (string.IsNullOrWhiteSpace(userId))
            return;

        var bidUsers = await bidService.GetBidUsers(bidId);
        if (bidUsers.Any(x => string.Equals(x, userId, StringComparison.OrdinalIgnoreCase)))
        {
            context.Succeed(requirement);
        }
    }

    private static string? TryGetBidId(object? resource)
    {
        if (resource is AuthorizationFilterContext mvcContext)
        {
            return mvcContext.RouteData.Values.TryGetValue("bidId", out var rawBidId)
                ? rawBidId?.ToString()
                : null;
        }

        if (resource is HttpContext httpContext)
        {
            return httpContext.Request.RouteValues.TryGetValue("bidId", out var rawBidId)
                ? rawBidId?.ToString()
                : null;
        }

        return null;
    }

    private async Task<string?> ResolveCurrentUserIdAsync(ClaimsPrincipal user)
    {
        var subject = user.FindFirst("sub")?.Value
                      ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var username = user.FindFirst("preferred_username")?.Value
                       ?? user.FindFirst(ClaimTypes.Name)?.Value;

        var users = await userService.GetUsers();

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
}
