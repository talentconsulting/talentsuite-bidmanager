using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TalentSuite.Server.Security;

public sealed class BidAccessAuthorizationHandler(
    ICurrentUserBidAuthorizationService currentUserBidAuthorizationService) : AuthorizationHandler<BidAccessRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        BidAccessRequirement requirement)
    {
        if (!context.User.Identity?.IsAuthenticated ?? true)
            return;

        var bidId = TryGetBidId(context.Resource);
        if (string.IsNullOrWhiteSpace(bidId))
            return;

        if (await currentUserBidAuthorizationService.HasBidAccessAsync(context.User, bidId))
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
}
