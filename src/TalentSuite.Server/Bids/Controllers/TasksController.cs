using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TalentSuite.Server.Bids.Services;
using TalentSuite.Server.Users.Services;
using TalentSuite.Shared.Tasks;

namespace TalentSuite.Server.Bids.Controllers;

[ApiController]
[Authorize]
[Route("api/tasks")]
public sealed class TasksController(IBidService bidService, IUserService userService) : ControllerBase
{
    [HttpGet("my")]
    public async Task<ActionResult<List<MentionTaskResponse>>> GetMyTasks(CancellationToken ct = default)
    {
        var userId = await GetCurrentUserIdAsync(User, ct);
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        var tasks = await bidService.GetMentionTasksForUser(userId, ct);
        return Ok(tasks);
    }

    [HttpGet("my-question-assignments")]
    public async Task<ActionResult<List<AssignedQuestionTaskResponse>>> GetMyQuestionAssignments(CancellationToken ct = default)
    {
        var userId = await GetCurrentUserIdAsync(User, ct);
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        var assignments = await bidService.GetAssignedQuestionsForUser(userId, ct);
        return Ok(assignments);
    }

    private async Task<string?> GetCurrentUserIdAsync(ClaimsPrincipal user, CancellationToken ct)
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
}
