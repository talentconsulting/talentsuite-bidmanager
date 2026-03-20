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
    public async Task<IActionResult> GetQuestionUsers(string bidId, string questionId)
    {
        var result = await _bidService.GetBidQuestionUsers(bidId, questionId);

        return Ok(result);
    }
    
    [HttpPost]
    [Authorize(Policy = "RequireAdminRole")]
    public async Task<IActionResult> AddBidUser(string bidId, string questionId, [FromBody] QuestionUserAssignmentRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        
        await _bidService.AddBidQuestionUser(bidId, questionId, request.UserId, request.Role);

        return Ok();
    }

    [HttpPut]
    [Authorize(Policy = "RequireAdminRole")]
    public async Task<IActionResult> UpdateBidUserRole(string bidId, string questionId, [FromBody] QuestionUserAssignmentRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        await _bidService.UpdateBidQuestionUserRole(bidId, questionId, request.UserId, request.Role);

        return Ok();
    }
    
    [HttpDelete()]
    [Authorize(Policy = "RequireAdminRole")]
    public async Task<IActionResult> RemoveBidUser(string bidId, string questionId, [FromBody] UserAssignmentRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        
        await _bidService.RemoveBidQuestionUser(bidId, questionId, request.UserId);

        return Ok();
    }
}
