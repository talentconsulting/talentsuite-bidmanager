using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;
using TalentSuite.Server.Bids.Services;
using TalentSuite.Shared;
using TalentSuite.Shared.Bids.Ai;

namespace TalentSuite.Server.Bids.Controllers;

[ApiController]
[Authorize(Policy = "RequireAdminRole")]
[Route("api/ai/questions")]
public class ChatQuestionController : ControllerBase   
{
    private readonly IBidService _bidService;
    private readonly IAzureOpenAiChatService _azureOpenAiChatService;
    private readonly ILogger<ChatQuestionController> _logger;

    public ChatQuestionController(
        IBidService bidService,
        IAzureOpenAiChatService azureOpenAiChatService,
        ILogger<ChatQuestionController> logger)
    {
        _bidService = bidService;
        _azureOpenAiChatService = azureOpenAiChatService;
        _logger = logger;
    }

    [HttpPost("{questionId}")]
    public async Task<IActionResult> AskQuestions(string questionId, [FromBody] ChatQuestionRequest chatQuestionRequest)
    {
        var resolvedQuestionId = string.IsNullOrWhiteSpace(chatQuestionRequest.QuestionId)
            ? questionId
            : chatQuestionRequest.QuestionId;

        try
        {
            var question = await _bidService.GetQuestion(chatQuestionRequest.BidId, resolvedQuestionId);

            var userId = ResolveCurrentUserKey();
            var persistedThreadId = string.IsNullOrWhiteSpace(userId)
                ? null
                : await _bidService.GetChatThreadId(
                    chatQuestionRequest.BidId,
                    resolvedQuestionId,
                    userId);

            var systemPrompt =
                $"Please use the bid library we have to return the answer to the question: ${question.Description}";

            var userPrompt = $"""{chatQuestionRequest.FreeTextQuestion}""";

            var result = await _azureOpenAiChatService.AskAsync(
                userPrompt,
                systemPrompt,
                chatQuestionRequest.ThreadId ?? persistedThreadId);

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
                    result.Response,
                    now.AddMilliseconds(1));
                await _bidService.SetChatThreadId(
                    chatQuestionRequest.BidId,
                    resolvedQuestionId,
                    userId,
                    result.ThreadId);
            }

            return Ok(new ChatQuestionResponse
            {
                Response = result.Response,
                ThreadId = result.ThreadId
            });
        }
        catch (ChatServiceUserException ex)
        {
            _logger.LogWarning(
                ex,
                "Chat request for bid {BidId}, question {QuestionId} returned a user-facing error.",
                chatQuestionRequest.BidId,
                chatQuestionRequest.QuestionId);
            return StatusCode(ex.StatusCode, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Chat request for bid {BidId}, question {QuestionId} failed unexpectedly.",
                chatQuestionRequest.BidId,
                chatQuestionRequest.QuestionId);
            throw;
        }
    }

    [HttpGet("{questionId}/messages")]
    public async Task<ActionResult<List<ChatMessageResponse>>> GetMessages(string questionId, [FromQuery] string bidId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(bidId))
            return BadRequest("bidId is required.");

        var userId = ResolveCurrentUserKey();
        if (string.IsNullOrWhiteSpace(userId))
            return Ok(new List<ChatMessageResponse>());

        var messages = await _bidService.GetChatMessages(bidId, questionId, userId, ct);
        return Ok(messages);
    }

    [HttpPost("{questionId}/stream")]
    public async Task StreamQuestion(string questionId, [FromBody] ChatQuestionRequest chatQuestionRequest, CancellationToken ct)
    {
        var resolvedQuestionId = string.IsNullOrWhiteSpace(chatQuestionRequest.QuestionId)
            ? questionId
            : chatQuestionRequest.QuestionId;

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
            var persistedThreadId = await _bidService.GetChatThreadId(chatQuestionRequest.BidId, resolvedQuestionId, userId, ct);
            var systemPrompt =
                $"Please use the bid library we have to return the answer to the question: ${question.Description}";
            var assistantResponse = new System.Text.StringBuilder();

            var startedAt = DateTimeOffset.UtcNow;
            await _bidService.AddChatMessage(
                chatQuestionRequest.BidId,
                resolvedQuestionId,
                userId,
                "user",
                chatQuestionRequest.FreeTextQuestion,
                startedAt,
                ct);

            string? threadId = null;
            await foreach (var update in _azureOpenAiChatService.StreamAsync(
                               chatQuestionRequest.FreeTextQuestion,
                               systemPrompt,
                               chatQuestionRequest.ThreadId ?? persistedThreadId,
                               ct))
            {
                if (!string.IsNullOrWhiteSpace(update.ThreadId))
                    threadId = update.ThreadId;

                if (string.Equals(update.Type, "delta", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(update.Content))
                    assistantResponse.Append(update.Content);

                await WriteStreamUpdateAsync(update, ct);
            }

            if (!string.IsNullOrWhiteSpace(threadId))
            {
                await _bidService.SetChatThreadId(
                    chatQuestionRequest.BidId,
                    resolvedQuestionId,
                    userId,
                    threadId,
                    ct);
            }

            if (assistantResponse.Length > 0)
            {
                await _bidService.AddChatMessage(
                    chatQuestionRequest.BidId,
                    resolvedQuestionId,
                    userId,
                    "assistant",
                    assistantResponse.ToString(),
                    DateTimeOffset.UtcNow,
                    ct);
            }
        }
        catch (ChatServiceUserException ex)
        {
            _logger.LogWarning(
                ex,
                "Streaming chat request for bid {BidId}, question {QuestionId} returned a user-facing error.",
                chatQuestionRequest.BidId,
                chatQuestionRequest.QuestionId);
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
                chatQuestionRequest.QuestionId);
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
}
