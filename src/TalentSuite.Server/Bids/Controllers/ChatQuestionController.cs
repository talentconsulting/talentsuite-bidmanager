using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TalentSuite.Server.Bids.Services;
using TalentSuite.Shared.Bids.Ai;

namespace TalentSuite.Server.Bids.Controllers;

[ApiController]
[Authorize(Policy = "RequireAdminRole")]
[Route("api/ai/questions/{Uri.EscapeDataString(q.Id)}")]
public class ChatQuestionController : ControllerBase   
{
    private readonly IBidService _bidService;
    private readonly IAzureOpenAiChatService _azureOpenAiChatService;

    public ChatQuestionController(IBidService bidService, IAzureOpenAiChatService azureOpenAiChatService)
    {
        _bidService = bidService;
        _azureOpenAiChatService = azureOpenAiChatService;
    }

    [HttpPost]
    public async Task<IActionResult> AskQuestions([FromBody] ChatQuestionRequest chatQuestionRequest)
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

    private string ResolveCurrentUserKey()
    {
        return User.FindFirst("sub")?.Value
               ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? User.FindFirst("preferred_username")?.Value
               ?? string.Empty;
    }
}
