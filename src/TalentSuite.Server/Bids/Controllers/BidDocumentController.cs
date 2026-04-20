using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TalentSuite.Server.Bids.Mappers;
using TalentSuite.Server.Bids.Services;
using TalentSuite.Shared;
using TalentSuite.Server.Bids.Services.Models;
using TalentSuite.Shared.Bids;
using System.Text.Json;

namespace TalentSuite.Server.Bids.Controllers;

[ApiController]
[Authorize(Policy = "RequireAdminRole")]
[Route("api/document")]
public sealed class BidDocumentController : ControllerBase
{
    private readonly IBidService _bidService;
    private readonly IDocumentIngestionJobService _jobService;
    private readonly BidMapper _mapper;

    public BidDocumentController(IBidService bidService, IDocumentIngestionJobService jobService, BidMapper mapper)
    {
        _bidService = bidService;
        _jobService = jobService;
        _mapper = mapper;
    }

    [HttpPost]
    [RequestSizeLimit(50_000_000)]
    public async Task<ActionResult<ParsedDocumentResponse>> Ingest(
        [FromForm] IFormFile file,
        [FromForm] BidStage stage,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest("File is required.");

        await using var stream = file.OpenReadStream();

        var result = await _bidService.ParseBidDocument(stream, file.FileName, stage, ct);

        return Ok(_mapper.ToResponse(result));
    }

    [HttpPost("jobs")]
    [RequestSizeLimit(50_000_000)]
    public async Task<ActionResult<DocumentIngestionJobCreatedResponse>> StartIngestionJob(
        [FromForm] IFormFile file,
        [FromForm] BidStage stage,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest("File is required.");

        await using var stream = file.OpenReadStream();
        await using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, ct);

        var userKey = ResolveCurrentUserKey();
        if (string.IsNullOrWhiteSpace(userKey))
            return Unauthorized();

        var jobId = _jobService.StartJob(userKey, memory.ToArray(), file.FileName, stage, ct);

        return Accepted(new DocumentIngestionJobCreatedResponse
        {
            JobId = jobId
        });
    }

    [HttpGet("jobs")]
    public async Task<ActionResult<List<DocumentIngestionJobStatusResponse>>> ListIngestionJobs(CancellationToken ct)
    {
        var userKey = ResolveCurrentUserKey();
        if (string.IsNullOrWhiteSpace(userKey))
            return Unauthorized();

        var jobs = await _jobService.ListJobsAsync(userKey, ct);
        return Ok(jobs);
    }

    [HttpGet("jobs/{jobId}")]
    public async Task<ActionResult<DocumentIngestionJobStatusResponse>> GetIngestionJob(string jobId, CancellationToken ct)
    {
        var userKey = ResolveCurrentUserKey();
        if (string.IsNullOrWhiteSpace(userKey))
            return Unauthorized();

        var job = await _jobService.GetJobAsync(jobId, userKey, ct);
        if (job is null)
            return NotFound();

        return Ok(job);
    }

    [HttpGet("jobs/{jobId}/stream")]
    public async Task<IActionResult> StreamIngestionJob(string jobId, CancellationToken ct)
    {
        var userKey = ResolveCurrentUserKey();
        if (string.IsNullOrWhiteSpace(userKey))
            return Unauthorized();

        var job = await _jobService.GetJobAsync(jobId, userKey, ct);
        if (job is null)
            return NotFound();

        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "application/x-ndjson";
        Response.Headers.CacheControl = "no-cache";

        await foreach (var update in _jobService.StreamJobAsync(jobId, ct))
        {
            var json = JsonSerializer.Serialize(update, SerialiserOptions.JsonOptions);
            await Response.WriteAsync(json, ct);
            await Response.WriteAsync("\n", ct);
            await Response.Body.FlushAsync(ct);
        }

        return new EmptyResult();
    }

    private string ResolveCurrentUserKey()
    {
        return User.FindFirst("sub")?.Value
               ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? User.FindFirst("preferred_username")?.Value
               ?? string.Empty;
    }
}
