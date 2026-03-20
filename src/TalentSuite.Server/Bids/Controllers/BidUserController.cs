using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TalentSuite.Server.Bids.Services;
using TalentSuite.Shared.Users;

namespace TalentSuite.Server.Bids.Controllers;

[ApiController]
[Authorize]
[Route("api/bids/{bidId}/users")]
public sealed class BidUserController : ControllerBase
{
    private readonly IBidService _bidService;

    public BidUserController(IBidService bidService)
    {
        _bidService = bidService;
    }
    
    [HttpGet]
    [Authorize(Policy = "RequireBidAccess")]
    public async Task<IActionResult> GetBidUsers(string bidId)
    {
        var result = await _bidService.GetBidUsers(bidId);

        return Ok(result);
    }
    
    [HttpPost]
    [Authorize(Policy = "RequireAdminRole")]
    public async Task<IActionResult> AddBidUser(string bidId, [FromBody] UserAssignmentRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        
        await _bidService.AddBidUser(bidId, request.UserId);

        return Ok();
    }
    
    [HttpDelete()]
    [Authorize(Policy = "RequireAdminRole")]
    public async Task<IActionResult> RemoveBidUser(string bidId, [FromBody] UserAssignmentRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        
        await _bidService.RemoveBidUser(bidId, request.UserId);

        return Ok();
    }
}
