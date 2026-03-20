using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TalentSuite.Server.Bids.Mappers;
using TalentSuite.Server.Bids.Services;
using TalentSuite.Server.Bids.Services.Models;
using TalentSuite.Shared.Bids;

namespace TalentSuite.Server.Bids.Controllers;

[ApiController]
[Authorize(Policy = "RequireAdminRole")]
[Route("api/document")]
public sealed class BidDocumentController : ControllerBase
{
    private readonly IBidService _bidService;
    private readonly BidMapper _mapper;

    public BidDocumentController(IBidService bidService, BidMapper mapper)
    {
        _bidService = bidService;
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
}
