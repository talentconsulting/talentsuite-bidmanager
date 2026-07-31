using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using TalentSuite.Server.Security;
using TalentSuite.Server.Bids.Chat;
using TalentSuite.Server.Bids.Services;
using TalentSuite.Server.Bids.Services.Models;
using TalentSuite.Shared;
using TalentSuite.Shared.Bids;
using TalentSuite.Shared.Bids.Ai;

namespace TalentSuite.Server.Bids.Controllers;

[ApiController]
[Authorize]
[Route("api/ai/questions")]
public class ChatQuestionController : ControllerBase   
{
    private const int MaxAttachmentFilesInContext = 4;
    private const int MaxAttachmentCharsPerFile = 8_000;
    private const int MaxAttachmentCharsTotal = 24_000;

    private readonly IBidService _bidService;
    private readonly IAzureOpenAiChatService _azureOpenAiChatService;
    private readonly IBidChatPolicyService _bidChatPolicyService;
    private readonly IDocumentIngestionservice _documentIngestionService;
    private readonly ILogger<ChatQuestionController> _logger;
    private readonly ICurrentUserBidAuthorizationService _authorizationService;

    public ChatQuestionController(
        IBidService bidService,
        IAzureOpenAiChatService azureOpenAiChatService,
        IBidChatPolicyService bidChatPolicyService,
        IDocumentIngestionservice documentIngestionService,
        ILogger<ChatQuestionController> logger,
        ICurrentUserBidAuthorizationService authorizationService)
    {
        _bidService = bidService;
        _azureOpenAiChatService = azureOpenAiChatService;
        _bidChatPolicyService = bidChatPolicyService;
        _documentIngestionService = documentIngestionService;
        _logger = logger;
        _authorizationService = authorizationService;
    }

    [HttpPost("{questionId}")]
    public async Task<IActionResult> AskQuestions(string questionId, [FromBody] ChatQuestionRequest chatQuestionRequest)
    {
        var resolvedQuestionIdResult = TryResolveQuestionId(questionId, chatQuestionRequest.QuestionId);
        if (resolvedQuestionIdResult.ErrorResult is not null)
            return BadRequest(resolvedQuestionIdResult.ErrorResult);

        var resolvedQuestionId = resolvedQuestionIdResult.QuestionId!;

        if (!await _authorizationService.CanManageBidAsync(User, chatQuestionRequest.BidId, HttpContext.RequestAborted))
            return Forbid();

        try
        {
            var question = await _bidService.GetQuestion(chatQuestionRequest.BidId, resolvedQuestionId);
            var policy = await _bidChatPolicyService.GetBidQuestionAnsweringPolicyAsync(HttpContext.RequestAborted);
            _bidChatPolicyService.ValidateRequest(
                policy,
                chatQuestionRequest.BidId,
                resolvedQuestionId,
                chatQuestionRequest.FreeTextQuestion,
                question);

            var userId = ResolveCurrentUserKey();
            var persistedThreadId = string.IsNullOrWhiteSpace(userId)
                ? null
                : await _bidService.GetChatThreadId(
                    chatQuestionRequest.BidId,
                    resolvedQuestionId,
                    userId);

            var systemPrompt = await BuildSystemPromptAsync(
                policy,
                chatQuestionRequest.BidId,
                chatQuestionRequest.BidFileIds,
                question,
                HttpContext.RequestAborted);

            var userPrompt = $"""{chatQuestionRequest.FreeTextQuestion}""";

            var result = await _azureOpenAiChatService.AskAsync(
                userPrompt,
                systemPrompt,
                chatQuestionRequest.ThreadId ?? persistedThreadId);

            var extractedResponse = _bidChatPolicyService.ExtractAnswerText(policy, result.Response);
            _bidChatPolicyService.ValidateResponse(policy, extractedResponse, result.Sources);

            if (!string.IsNullOrWhiteSpace(userId))
            {
                var now = DateTimeOffset.UtcNow;
                await _bidService.AddChatMessage(
                    chatQuestionRequest.BidId,
                    resolvedQuestionId,
                    userId,
                    "user",
                    chatQuestionRequest.FreeTextQuestion,
                    now);
                await _bidService.AddChatMessage(
                    chatQuestionRequest.BidId,
                    resolvedQuestionId,
                    userId,
                    "assistant",
                    extractedResponse,
                    now.AddMilliseconds(1),
                    result.Sources,
                    result.UsedSourcesOutsideBidLibrary);
                await _bidService.SetChatThreadId(
                    chatQuestionRequest.BidId,
                    resolvedQuestionId,
                    userId,
                    result.ThreadId);
            }

            return Ok(new ChatQuestionResponse
            {
                Response = extractedResponse,
                ThreadId = result.ThreadId,
                Sources = result.Sources,
                UsedSourcesOutsideBidLibrary = result.UsedSourcesOutsideBidLibrary
            });
        }
        catch (ChatServiceUserException ex)
        {
            _logger.LogWarning(
                ex,
                "Chat request for bid {BidId}, question {QuestionId} returned a user-facing error.",
                chatQuestionRequest.BidId,
                resolvedQuestionId);
            return StatusCode(ex.StatusCode, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Chat request for bid {BidId}, question {QuestionId} failed unexpectedly.",
                chatQuestionRequest.BidId,
                resolvedQuestionId);
            throw;
        }
    }

    [HttpGet("{questionId}/messages")]
    public async Task<ActionResult<List<ChatMessageResponse>>> GetMessages(string questionId, [FromQuery] string bidId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(bidId))
            return BadRequest("bidId is required.");

        if (!await _authorizationService.CanManageBidAsync(User, bidId, ct))
            return Forbid();

        var userId = ResolveCurrentUserKey();
        if (string.IsNullOrWhiteSpace(userId))
            return Ok(new List<ChatMessageResponse>());

        var messages = await _bidService.GetChatMessages(bidId, questionId, userId, ct);
        return Ok(messages);
    }

    [HttpPost("{questionId}/stream")]
    public async Task StreamQuestion(string questionId, [FromBody] ChatQuestionRequest chatQuestionRequest, CancellationToken ct)
    {
        var resolvedQuestionIdResult = TryResolveQuestionId(questionId, chatQuestionRequest.QuestionId);
        if (resolvedQuestionIdResult.ErrorResult is not null)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await WriteStreamUpdateAsync(new ChatStreamUpdate
            {
                Type = "error",
                Error = resolvedQuestionIdResult.ErrorResult
            }, ct);
            return;
        }

        var resolvedQuestionId = resolvedQuestionIdResult.QuestionId!;

        if (!await _authorizationService.CanManageBidAsync(User, chatQuestionRequest.BidId, ct))
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var userId = ResolveCurrentUserKey();
        if (string.IsNullOrWhiteSpace(userId))
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "application/x-ndjson";
        Response.Headers.CacheControl = "no-cache";

        try
        {
            var question = await _bidService.GetQuestion(chatQuestionRequest.BidId, resolvedQuestionId);
            var policy = await _bidChatPolicyService.GetBidQuestionAnsweringPolicyAsync(ct);
            _bidChatPolicyService.ValidateRequest(
                policy,
                chatQuestionRequest.BidId,
                resolvedQuestionId,
                chatQuestionRequest.FreeTextQuestion,
                question);

            var persistedThreadId = await _bidService.GetChatThreadId(chatQuestionRequest.BidId, resolvedQuestionId, userId, ct);
            var systemPrompt = await BuildSystemPromptAsync(
                policy,
                chatQuestionRequest.BidId,
                chatQuestionRequest.BidFileIds,
                question,
                ct);
            var rawAssistantResponse = new System.Text.StringBuilder();
            var completedAssistantResponse = string.Empty;
            var emittedAssistantResponse = string.Empty;
            List<ChatSourceReferenceResponse> assistantSources = [];
            var usedSourcesOutsideBidLibrary = false;

            var startedAt = DateTimeOffset.UtcNow;
            await _bidService.AddChatMessage(
                chatQuestionRequest.BidId,
                resolvedQuestionId,
                userId,
                "user",
                chatQuestionRequest.FreeTextQuestion,
                startedAt,
                ct: ct);

            string? threadId = null;
            await foreach (var update in _azureOpenAiChatService.StreamAsync(
                               chatQuestionRequest.FreeTextQuestion,
                               systemPrompt,
                               chatQuestionRequest.ThreadId ?? persistedThreadId,
                               ct))
            {
                if (!string.IsNullOrWhiteSpace(update.ThreadId))
                    threadId = update.ThreadId;

                if (string.Equals(update.Type, "delta", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(update.Content))
                {
                    rawAssistantResponse.Append(update.Content);
                    continue;
                }

                if (string.Equals(update.Type, "completed", StringComparison.OrdinalIgnoreCase))
                {
                    completedAssistantResponse = update.Content ?? string.Empty;
                    // The completed message is canonical and retains all whitespace even
                    // when an SDK or intermediary has altered individual stream deltas.
                    var rawResponse = !string.IsNullOrEmpty(completedAssistantResponse)
                        ? completedAssistantResponse
                        : rawAssistantResponse.ToString();
                    var extractedFinal = _bidChatPolicyService.ExtractAnswerText(policy, rawResponse);

                    if (!string.IsNullOrWhiteSpace(extractedFinal))
                    {
                        emittedAssistantResponse = extractedFinal;
                        await WriteStreamUpdateAsync(new ChatStreamUpdate
                        {
                            Type = "delta",
                            ThreadId = update.ThreadId,
                            Content = extractedFinal
                        }, ct);
                    }

                    assistantSources = update.Sources ?? [];
                    usedSourcesOutsideBidLibrary = update.UsedSourcesOutsideBidLibrary;
                }

                await WriteStreamUpdateAsync(update, ct);
            }

            var finalRawResponse = !string.IsNullOrEmpty(completedAssistantResponse)
                ? completedAssistantResponse
                : rawAssistantResponse.ToString();
            var finalAssistantResponse = !string.IsNullOrWhiteSpace(emittedAssistantResponse)
                ? emittedAssistantResponse
                : _bidChatPolicyService.ExtractAnswerText(policy, finalRawResponse);
            _bidChatPolicyService.ValidateResponse(policy, finalAssistantResponse, assistantSources);

            if (!string.IsNullOrWhiteSpace(threadId))
            {
                await _bidService.SetChatThreadId(
                    chatQuestionRequest.BidId,
                    resolvedQuestionId,
                    userId,
                    threadId,
                    ct);
            }

            if (!string.IsNullOrWhiteSpace(finalAssistantResponse))
            {
                await _bidService.AddChatMessage(
                    chatQuestionRequest.BidId,
                    resolvedQuestionId,
                    userId,
                    "assistant",
                    finalAssistantResponse,
                    DateTimeOffset.UtcNow,
                    assistantSources,
                    usedSourcesOutsideBidLibrary,
                    ct);
            }
        }
        catch (ChatServiceUserException ex)
        {
            _logger.LogWarning(
                ex,
                "Streaming chat request for bid {BidId}, question {QuestionId} returned a user-facing error.",
                chatQuestionRequest.BidId,
                resolvedQuestionId);
            await WriteStreamUpdateAsync(new ChatStreamUpdate
            {
                Type = "error",
                Error = ex.Message
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Streaming chat request for bid {BidId}, question {QuestionId} failed unexpectedly.",
                chatQuestionRequest.BidId,
                resolvedQuestionId);
            await WriteStreamUpdateAsync(new ChatStreamUpdate
            {
                Type = "error",
                Error = "Chat failed unexpectedly."
            }, ct);
        }
    }

    private async Task WriteStreamUpdateAsync(ChatStreamUpdate update, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(update, SerialiserOptions.JsonOptions);
        await Response.WriteAsync(json, ct);
        await Response.WriteAsync("\n", ct);
        await Response.Body.FlushAsync(ct);
    }

    private string ResolveCurrentUserKey()
    {
        return User.FindFirst("sub")?.Value
               ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? User.FindFirst("preferred_username")?.Value
               ?? string.Empty;
    }

    private async Task<string> BuildSystemPromptAsync(
        BidChatPolicyDefinition policy,
        string bidId,
        IReadOnlyCollection<string>? selectedFileIds,
        TalentSuite.Server.Bids.Services.Models.CreateQuestionModel question,
        CancellationToken ct)
    {
        var basePrompt = _bidChatPolicyService.BuildSystemPrompt(policy, question);
        var attachmentContext = await BuildBidAttachmentContextAsync(bidId, selectedFileIds, ct);

        return string.IsNullOrWhiteSpace(attachmentContext)
            ? basePrompt
            : $"{basePrompt}\n\n{attachmentContext}";
    }

    private async Task<string> BuildBidAttachmentContextAsync(
        string bidId,
        IReadOnlyCollection<string>? selectedFileIds,
        CancellationToken ct)
    {
        if (selectedFileIds is null || selectedFileIds.Count == 0)
            return string.Empty;

        var files = await _bidService.GetBidFiles(bidId, ct);
        if (files.Count == 0)
            return string.Empty;

        var filesById = files.ToDictionary(file => file.Id, StringComparer.OrdinalIgnoreCase);
        var selectedFiles = selectedFileIds
            .Where(fileId => !string.IsNullOrWhiteSpace(fileId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxAttachmentFilesInContext)
            .Select(fileId => filesById.GetValueOrDefault(fileId))
            .Where(file => file is not null)
            .Cast<BidFileResponse>();

        var prompt = new StringBuilder();
        var remainingBudget = MaxAttachmentCharsTotal;

        foreach (var file in selectedFiles)
        {
            if (remainingBudget <= 0)
                break;

            var fileContent = await _bidService.GetBidFile(bidId, file.Id, ct);
            if (fileContent is null || fileContent.Content.Length == 0)
                continue;

            var extractedText = await ExtractBidAttachmentTextAsync(fileContent, ct);
            if (string.IsNullOrWhiteSpace(extractedText))
                continue;

            var boundedText = extractedText.Length > MaxAttachmentCharsPerFile
                ? extractedText[..MaxAttachmentCharsPerFile]
                : extractedText;

            if (boundedText.Length > remainingBudget)
                boundedText = boundedText[..remainingBudget];

            if (string.IsNullOrWhiteSpace(boundedText))
                continue;

            if (prompt.Length == 0)
            {
                prompt.AppendLine("Additional bid attachment context:");
                prompt.AppendLine("- Use this material to understand the specific buyer, supplier, scope, terminology, and requirements for this bid.");
                prompt.AppendLine("- Treat it as bid-specific background context.");
                prompt.AppendLine("- Prefer bid-library evidence for reusable capability claims and cited examples when available.");
                prompt.AppendLine();
            }

            prompt.AppendLine($"Attachment: {fileContent.FileName}");
            prompt.AppendLine(boundedText.Trim());
            prompt.AppendLine();

            remainingBudget -= boundedText.Length;
        }

        return prompt.ToString().Trim();
    }

    private async Task<string> ExtractBidAttachmentTextAsync(BidFileContentModel file, CancellationToken ct)
    {
        try
        {
            await using var stream = new MemoryStream(file.Content, writable: false);
            return await _documentIngestionService.ExtractTextAsync(stream, file.FileName, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract chat context from bid attachment {FileName}.", file.FileName);
            return string.Empty;
        }
    }

    private static (string? QuestionId, string? ErrorResult) TryResolveQuestionId(string routeQuestionId, string? bodyQuestionId)
    {
        if (string.IsNullOrWhiteSpace(routeQuestionId))
            return (null, "questionId route parameter is required.");

        if (string.IsNullOrWhiteSpace(bodyQuestionId))
            return (routeQuestionId, null);

        if (!string.Equals(routeQuestionId, bodyQuestionId, StringComparison.Ordinal))
            return (null, "questionId in the route must match questionId in the request body.");

        return (routeQuestionId, null);
    }

}
