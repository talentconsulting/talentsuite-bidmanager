using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TalentSuite.Server.Users.Mappers;
using TalentSuite.Server.Users.Services;
using TalentSuite.Server.Users.Services.Models;
using TalentSuite.Shared.Users;

namespace TalentSuite.Server.Users.Controllers;

[ApiController]
[Authorize]
[Route("api/users")]
public class UserInvitesController : ControllerBase
{
    private readonly UserMapper _mapper;
    private readonly IUserService _userService;

    public UserInvitesController(UserMapper mapper, IUserService userService)
    {
        _mapper = mapper;
        _userService = userService;
    }

    [HttpGet("me")]
    public async Task<ActionResult<CurrentUserProfileResponse>> GetCurrentUserProfile(CancellationToken ct)
    {
        var user = await ResolveCurrentUserAsync(ct);
        if (user is null)
            return NotFound("Current user is not linked to an internal user profile.");

        return Ok(new CurrentUserProfileResponse
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email,
            Role = user.Role,
            HasAcceptedRegistration = user.HasAcceptedRegistration,
            IdentityProvider = user.IdentityProvider,
            IdentitySubject = user.IdentitySubject,
            IdentityUsername = user.IdentityUsername
        });
    }

    [HttpGet("me-identity-debug")]
    public async Task<ActionResult<object>> GetCurrentIdentityDebug(CancellationToken ct)
    {
        var subject = User.FindFirst("sub")?.Value
                      ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var username = User.FindFirst("preferred_username")?.Value
                       ?? User.FindFirst(ClaimTypes.Name)?.Value;
        var email = User.FindFirst("email")?.Value
                    ?? User.FindFirst(ClaimTypes.Email)?.Value;

        var matchedUser = await ResolveCurrentUserAsync(ct);

        return Ok(new
        {
            claims = new
            {
                sub = User.FindFirst("sub")?.Value,
                preferred_username = User.FindFirst("preferred_username")?.Value,
                email = User.FindFirst("email")?.Value,
                nameidentifier = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                name = User.FindFirst(ClaimTypes.Name)?.Value,
                roles = User.FindAll(ClaimTypes.Role).Select(x => x.Value).Distinct().ToList()
            },
            matchedInternalUser = matchedUser is null
                ? null
                : new
                {
                    matchedUser.Id,
                    matchedUser.Name,
                    matchedUser.Email,
                    matchedUser.Role,
                    matchedUser.IdentityProvider,
                    matchedUser.IdentitySubject,
                    matchedUser.IdentityUsername,
                    matchedUser.HasAcceptedRegistration
                },
            matchHint = matchedUser is null
                ? "No internal user matched by IdentitySubject/IdentityUsername/Id."
                : "Matched internal user successfully.",
            evaluated = new
            {
                subject,
                username,
                email
            }
        });
    }

    [HttpGet("me-authorisation")]
    public ActionResult<object> GetCurrentAuthorisation()
    {
        var roles = User.FindAll(ClaimTypes.Role)
            .Select(x => x.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var isAdmin = roles.Any(r =>
            string.Equals(r, "admin", StringComparison.OrdinalIgnoreCase));

        return Ok(new
        {
            isAdmin,
            roles
        });
    }

    [HttpPost("accept-invite")]
    public async Task<ActionResult<UserResponse>> AcceptInvite([FromBody] AcceptInviteRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        if (string.IsNullOrWhiteSpace(request.InvitationToken))
            return BadRequest("Invitation token is required.");

        var identitySubject = User.FindFirst("sub")?.Value
                              ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(identitySubject))
            return Unauthorized("Identity subject claim was not found.");

        var identityUsername = User.FindFirst("preferred_username")?.Value
                               ?? User.FindFirst(ClaimTypes.Name)?.Value
                               ?? string.Empty;
        var email = User.FindFirst(ClaimTypes.Email)?.Value
                    ?? User.FindFirst("email")?.Value
                    ?? string.Empty;

        var user = await _userService.AcceptInvite(
            request.InvitationToken,
            "keycloak",
            identitySubject,
            identityUsername,
            email,
            ct);
        if (user is null)
            return BadRequest("Invite acceptance failed.");

        return Ok(_mapper.ToResponse(user));
    }

    [AllowAnonymous]
    [HttpPost("accept-invite/register")]
    public async Task<ActionResult<UserResponse>> RegisterFromInvite([FromBody] RegisterInviteRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        if (string.IsNullOrWhiteSpace(request.InvitationToken))
            return BadRequest("Invitation token is required.");

        if (string.IsNullOrWhiteSpace(request.Username))
            return BadRequest("Username is required.");

        if (string.IsNullOrWhiteSpace(request.Password))
            return BadRequest("Password is required.");

        var user = await _userService.RegisterInvitedUser(
            request.InvitationToken,
            request.Username,
            request.Password,
            ct);
        if (user is null)
            return BadRequest("Invite registration failed.");

        return Ok(_mapper.ToResponse(user));
    }

    private async Task<UserModel?> ResolveCurrentUserAsync(CancellationToken ct)
    {
        var subject = User.FindFirst("sub")?.Value
                      ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var username = User.FindFirst("preferred_username")?.Value
                       ?? User.FindFirst(ClaimTypes.Name)?.Value;

        var users = await _userService.GetUsers(ct);

        var match = users.FirstOrDefault(u =>
            (!string.IsNullOrWhiteSpace(subject) && string.Equals(u.IdentitySubject, subject, StringComparison.Ordinal)) ||
            (!string.IsNullOrWhiteSpace(username) && string.Equals(u.IdentityUsername, username, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(subject) && string.Equals(u.Id, subject, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(username) && string.Equals(u.Id, username, StringComparison.OrdinalIgnoreCase)));

        return match;
    }
}
