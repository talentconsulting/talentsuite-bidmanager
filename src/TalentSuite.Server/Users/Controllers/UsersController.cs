using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TalentSuite.Server.Users.Mappers;
using TalentSuite.Server.Users.Services;
using TalentSuite.Server.Users.Services.Models;
using TalentSuite.Shared.Users;

namespace TalentSuite.Server.Users.Controllers;

[ApiController]
[Authorize(Policy = "RequireAdminRole")]
[Route("api/users")]
public class UsersController: ControllerBase
{
    private readonly UserMapper _mapper;
    private readonly IUserService _userService;

    public UsersController(UserMapper mapper, IUserService userService)
    {
        _mapper = mapper;
        _userService = userService;
    }

    [HttpGet]
    public async Task<ActionResult<List<UserResponse>>> GetUsers(CancellationToken ct)
    {
        var users = await _userService.GetUsers(ct);
        
        return Ok(_mapper.ToResponses(users));
    }

    [HttpPost]
    public async Task<ActionResult<UserResponse>> AddUser([FromBody] CreateUserRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var created = await _userService.AddUser(new UserModel
        {
            Name = request.Name,
            Email = request.Email,
            Role = request.Role,
            HasAcceptedRegistration = request.HasAcceptedRegistration
        }, ct);

        var response = _mapper.ToResponse(created);
        return CreatedAtAction(nameof(GetUser), new { userId = response.Id }, response);
    }

    [HttpGet("{userId}")]
    public async Task<ActionResult<UserResponse>> GetUser(string userId, CancellationToken ct)
    {
        var user = await _userService.GetUser(userId, ct);
        if (user is null)
            return NotFound();

        return Ok(_mapper.ToResponse(user));
    }

    [HttpPut("{userId}")]
    public async Task<IActionResult> UpdateUser(string userId, [FromBody] UpdateUserRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var userModel = new UserModel
        {
            Id = userId,
            Name = request.Name,
            Email = request.Email,
            Role = request.Role,
            HasAcceptedRegistration = request.HasAcceptedRegistration
        };

        var updated = await _userService.UpdateUser(userId, userModel, ct);
        if (!updated)
            return NotFound();

        return NoContent();
    }

    [HttpPost("{userId}/resend-invite")]
    public async Task<ActionResult<ResendInviteResponse>> ResendInvite(string userId, CancellationToken ct)
    {
        var user = await _userService.ResendInvite(userId, ct);
        if (user is null)
            return BadRequest("Invite could not be resent.");

        return Ok(new ResendInviteResponse
        {
            InvitationToken = user.InvitationToken,
            InvitationExpiresAtUtc = user.InvitationExpiresAtUtc
        });
    }

    [HttpDelete("{userId}")]
    public async Task<IActionResult> DeleteUser(string userId, CancellationToken ct)
    {
        var deleted = await _userService.DeleteUser(userId, ct);
        if (!deleted)
            return BadRequest("User could not be deleted.");

        return NoContent();
    }

}
