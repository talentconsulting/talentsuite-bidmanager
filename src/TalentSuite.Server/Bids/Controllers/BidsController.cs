using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TalentSuite.Server.Bids.Mappers;
using TalentSuite.Server.Bids.Services;
using TalentSuite.Server.Bids.Services.Models;
using TalentSuite.Shared.Bids;
using TalentSuite.Shared.Bids.List;
using TalentSuite.Shared.Messaging;
using TalentSuite.Shared.Messaging.Events;

namespace TalentSuite.Server.Bids.Controllers;

[ApiController]
[Authorize]
[Route("api/bids")]
public sealed class BidsController : ControllerBase
{
    private readonly IBidService _bidService;
    private readonly BidMapper _mapper;
    private readonly IAzureServiceBusClient _azureServiceBusClient;
    private readonly string _bidSubmittedEntityName;

    public BidsController(
        IBidService bidService,
        BidMapper mapper,
        IAzureServiceBusClient azureServiceBusClient,
        IConfiguration configuration)
    {
        _bidService = bidService;
        _mapper = mapper;
        _azureServiceBusClient = azureServiceBusClient;
        _bidSubmittedEntityName = configuration["AzureServiceBus:BidSubmittedEntityName"] ?? "bid-submitted";
    }
    
    [HttpPost]
    [Authorize(Policy = "RequireAdminRole")]
    public async Task<IActionResult> Create([FromBody] CreateBidRequest req)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        
        var result = await _bidService.CreateBid(_mapper.ToModel(req));
        
        return CreatedAtAction(
            nameof(Get),                // Action name
            new { result },   // Route values
            new { result }              // Response body (optional)
        ); 
    }
    
    // GET api/bids/{bidId}
    [HttpGet("{bidId}")]
    [Authorize(Policy = "RequireBidAccess")]
    public async Task<ActionResult<BidResponse>> Get(string bidId, CancellationToken ct)
    {
        var model = await _bidService.GetBid(bidId, ct);
        if (model is null)
            return NotFound();

        return Ok(_mapper.ToResponse(model));
    }
    
    [HttpGet]
    [Authorize(Policy = "RequireAdminRole")]
    public async Task<ActionResult<PagedBidListResponse>> Get(int page, int pageSize, CancellationToken ct)
    {
        var model = await _bidService.SearchBids(page, pageSize, ct);

        var response = _mapper.ToResponse(model);
        
        return Ok(response);
    }

    [HttpPatch("{bidId}/status")]
    [Authorize(Policy = "RequireBidAccess")]
    public async Task<IActionResult> SetStatus(
        string bidId,
        [FromBody] UpdateBidStatusRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        await _bidService.SetBidStatus(bidId, request.Status, ct);

        return Ok();
    }

    [HttpGet("{bidId}/files")]
    [Authorize(Policy = "RequireBidAccess")]
    public async Task<ActionResult<List<BidFileResponse>>> GetFiles(string bidId, CancellationToken ct)
    {
        var files = await _bidService.GetBidFiles(bidId, ct);
        return Ok(files);
    }

    [HttpPost("{bidId}/files")]
    [Authorize(Policy = "RequireBidAccess")]
    [RequestSizeLimit(25_000_000)]
    public async Task<ActionResult<BidFileResponse>> UploadFile(
        string bidId,
        [FromForm] IFormFile file,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest("No file was provided.");

        await using var stream = file.OpenReadStream();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, ct);

        var uploaded = await _bidService.AddBidFile(
            bidId,
            file.FileName,
            file.ContentType,
            memory.ToArray(),
            ct);

        return Ok(uploaded);
    }

    [HttpGet("{bidId}/files/{fileId}")]
    [Authorize(Policy = "RequireBidAccess")]
    public async Task<IActionResult> DownloadFile(string bidId, string fileId, CancellationToken ct)
    {
        var file = await _bidService.GetBidFile(bidId, fileId, ct);
        if (file is null)
            return NotFound();

        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpDelete("{bidId}/files/{fileId}")]
    [Authorize(Policy = "RequireBidAccess")]
    public async Task<IActionResult> DeleteFile(string bidId, string fileId, CancellationToken ct)
    {
        var deleted = await _bidService.DeleteBidFile(bidId, fileId, ct);
        if (!deleted)
            return NotFound();

        return NoContent();
    }

    [HttpPost("{bidId}/library-push")]
    [Authorize(Policy = "RequireBidAccess")]
    public async Task<ActionResult<BidLibraryPushResponse>> PushToBidLibrary(
        string bidId,
        CancellationToken ct)
    {
        var userId = User.FindFirst("sub")?.Value
                     ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        var userName = User.FindFirst("preferred_username")?.Value
                       ?? User.FindFirst(ClaimTypes.Name)?.Value
                       ?? User.FindFirst(ClaimTypes.Email)?.Value
                       ?? userId;

        var result = await _bidService.PushBidToLibrary(
            bidId,
            userId,
            userName ?? string.Empty,
            DateTime.UtcNow,
            ct);

        await PublishBidLibraryPushEventAsync(bidId, ct);

        return Ok(result);
    }

    private async Task PublishBidLibraryPushEventAsync(string bidId, CancellationToken ct)
    {
        var model = await _bidService.GetBid(bidId, ct);
        var bid = _mapper.ToResponse(model);
        var finalAnswerTextByQuestionId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var question in bid.Questions.Where(q => !string.IsNullOrWhiteSpace(q.Id)))
        {
            var answer = await _bidService.GetFinalAnswer(bidId, question.Id, ct);
            finalAnswerTextByQuestionId[question.Id] = answer?.AnswerText ?? string.Empty;
        }

        await _azureServiceBusClient.PublishAsync(
            _bidSubmittedEntityName,
            new BidSubmittedEvent
            {
                BidId = bidId,
                Bid = bid,
                FinalAnswerTextByQuestionId = finalAnswerTextByQuestionId
            },
            ct);
    }
}
