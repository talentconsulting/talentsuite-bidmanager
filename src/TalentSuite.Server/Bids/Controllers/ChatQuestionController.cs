using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TalentSuite.Server.Bids.Services;
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
        if (!string.Equals(questionId, chatQuestionRequest.QuestionId, StringComparison.OrdinalIgnoreCase))
            return BadRequest("Route question id does not match request body.");

        try
        {
            var question = await _bidService.GetQuestion(chatQuestionRequest.BidId, chatQuestionRequest.QuestionId);

            var userId = ResolveCurrentUserKey();
            var persistedThreadId = string.IsNullOrWhiteSpace(userId)
                ? null
                : await _bidService.GetChatThreadId(
                    chatQuestionRequest.BidId,
                    chatQuestionRequest.QuestionId,
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
                await _bidService.SetChatThreadId(
                    chatQuestionRequest.BidId,
                    chatQuestionRequest.QuestionId,
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

    private string ResolveCurrentUserKey()
    {
        return User.FindFirst("sub")?.Value
               ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? User.FindFirst("preferred_username")?.Value
               ?? string.Empty;
    }
}
