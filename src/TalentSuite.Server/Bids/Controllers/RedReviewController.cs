using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TalentSuite.Server.Bids.Services;
using TalentSuite.Shared.Bids;

namespace TalentSuite.Server.Bids.Controllers;

[ApiController]
[Authorize(Policy = "RequireBidAccess")]
[Route("api/bids/{bidId}/questions/{questionId}/red-review")]
public sealed class RedReviewController(IBidService bidService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(string bidId, string questionId, CancellationToken ct = default)
    {
        var review = await bidService.GetRedReview(bidId, questionId, ct);
        if (review is null)
            return NotFound();

        return Ok(review);
    }

    [HttpPut]
    public async Task<IActionResult> Set(string bidId, string questionId, [FromBody] UpdateRedReviewRequest request, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        await bidService.SetRedReview(bidId, questionId, request, ct);
        return Ok();
    }

    [HttpPost("comments")]
    public async Task<IActionResult> AddComment(
        string bidId,
        string questionId,
        [FromBody] AddDraftCommentRequest request,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var text = request.Comment?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return BadRequest("Comment cannot be empty.");

        request.Comment = text;
        if (request.StartIndex.HasValue && request.EndIndex.HasValue && request.EndIndex.Value < request.StartIndex.Value)
            (request.StartIndex, request.EndIndex) = (request.EndIndex, request.StartIndex);
        var result = await bidService.AddRedReviewComment(bidId, questionId, request, ct);
        return Ok(result);
    }

    [HttpPatch("comments/{commentId}")]
    public async Task<IActionResult> SetCommentCompletion(
        string bidId,
        string questionId,
        string commentId,
        [FromBody] SetCommentCompletionRequest request,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        if (string.IsNullOrWhiteSpace(commentId))
            return BadRequest("Comment id is required.");

        var result = await bidService.SetRedReviewCommentCompletion(
            bidId,
            questionId,
            commentId,
            request.IsComplete,
            ct);
        return Ok(result);
    }
}
