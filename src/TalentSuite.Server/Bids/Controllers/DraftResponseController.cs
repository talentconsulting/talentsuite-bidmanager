using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TalentSuite.Server.Bids.Mappers;
using TalentSuite.Server.Bids.Services;
using TalentSuite.Server.Bids.Services.Models;
using TalentSuite.Shared.Bids;

namespace TalentSuite.Server.Bids.Controllers;

[ApiController]
[Authorize(Policy = "RequireBidAccess")]
[Route("api/bids/{bidId}/questions/{questionId}/drafts")]
public sealed class DraftResponseController(BidMapper mapper, IBidService bidService) : ControllerBase
{
    private readonly BidMapper _mapper = mapper;

    [HttpPost]
    public async Task<IActionResult> AddResponse(string bidId, string questionId, [FromBody] DraftRequest request, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var result = await bidService.AddDraft(bidId, questionId, request.Response, ct);
        
        return Ok(new CreateAssetResponse(result));
    }

    [HttpDelete]
    public async Task<IActionResult> RemoveResponse(string bidId, string questionId, string responseId, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        await bidService.RemoveDraft(bidId, questionId, responseId, ct);
        
        return Ok();
    }
    
    [HttpPut("{draftId}")]
    public async Task<IActionResult> UpdateResponse(string bidId, string questionId, string draftId, [FromBody]UpdateDraftRequest request, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        await bidService.UpdateDraft(bidId, questionId, draftId, request.Response, ct);
        
        return Ok();
    }
    
    [HttpGet]
    public async Task<IActionResult> GetAllResponses(string bidId, string questionId, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var result = await bidService.GetQuestionDrafts(bidId, questionId, ct);
        
        return Ok(_mapper.ToDraftResponses(result));
    }

    [HttpGet("{draftId}/comments")]
    public async Task<IActionResult> GetComments(string bidId, string questionId, string draftId, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var result = await bidService.GetDraftComments(bidId, questionId, draftId, ct);
        return Ok(_mapper.ToDraftCommentResponses(result));
    }

    [HttpPost("{draftId}/comments")]
    public async Task<IActionResult> AddComment(string bidId, string questionId, string draftId, [FromBody] AddDraftCommentRequest request, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var text = request.Comment?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return BadRequest("Comment cannot be empty.");

        var authorName = request.AuthorName?.Trim() ?? string.Empty;
        var userId = request.UserId?.Trim() ?? string.Empty;
        var selectedText = request.SelectedText ?? string.Empty;
        var start = request.StartIndex;
        var end = request.EndIndex;
        if (start.HasValue && end.HasValue && end.Value < start.Value)
        {
            (start, end) = (end, start);
        }

        var result = await bidService.AddDraftComment(
            bidId,
            questionId,
            draftId,
            text,
            userId,
            authorName,
            request.MentionedUserIds ?? new List<string>(),
            start,
            end,
            selectedText,
            ct);
        return Ok(_mapper.ToResponse(result));
    }

    [HttpPatch("{draftId}/comments/{commentId}")]
    public async Task<IActionResult> SetCommentCompletion(
        string bidId,
        string questionId,
        string draftId,
        string commentId,
        [FromBody] SetCommentCompletionRequest request,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        if (string.IsNullOrWhiteSpace(commentId))
            return BadRequest("Comment id is required.");

        var result = await bidService.SetDraftCommentCompletion(
            bidId,
            questionId,
            draftId,
            commentId,
            request.IsComplete,
            ct);
        return Ok(result);
    }
}
