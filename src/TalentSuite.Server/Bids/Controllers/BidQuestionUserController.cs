using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TalentSuite.Server.Bids.Services;
using TalentSuite.Shared.Bids;
using TalentSuite.Shared.Users;

namespace TalentSuite.Server.Bids.Controllers;

[ApiController]
[Authorize]
[Route("api/bids/{bidId}/questions/{questionId}/users")]
public sealed class BidQuestionUserController : ControllerBase
{
    private readonly IBidService _bidService;

    public BidQuestionUserController(IBidService bidService)
    {
        _bidService = bidService;
    }
    
    [HttpGet]
    [Authorize(Policy = "RequireBidAccess")]
    public async Task<IActionResult> GetQuestionUsers(string bidId, string questionId, CancellationToken ct)
    {
        var result = await _bidService.GetBidQuestionUsers(bidId, questionId, ct);

        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = "RequireBidManagementRole")]
    public async Task<IActionResult> AddBidUser(string bidId, string questionId, [FromBody] QuestionUserAssignmentRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        await _bidService.AddBidQuestionUser(bidId, questionId, request.UserId, request.Role, ct);

        return Ok();
    }

    [HttpPut]
    [Authorize(Policy = "RequireBidManagementRole")]
    public async Task<IActionResult> UpdateBidUserRole(string bidId, string questionId, [FromBody] QuestionUserAssignmentRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        await _bidService.UpdateBidQuestionUserRole(bidId, questionId, request.UserId, request.Role, ct);

        return Ok();
    }

    [HttpDelete()]
    [Authorize(Policy = "RequireBidManagementRole")]
    public async Task<IActionResult> RemoveBidUser(string bidId, string questionId, [FromBody] UserAssignmentRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        await _bidService.RemoveBidQuestionUser(bidId, questionId, request.UserId, ct);

        return Ok();
    }
}
