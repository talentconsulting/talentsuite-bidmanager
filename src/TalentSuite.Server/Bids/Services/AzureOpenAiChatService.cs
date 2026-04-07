using System.Text;
using Azure;
using Azure.AI.Agents.Persistent;
using Azure.Core;
using Azure.Identity;

namespace TalentSuite.Server.Bids.Services;

public interface IAzureOpenAiChatService
{
    Task<ChatAnswerResult> AskAsync(
        string userPrompt,
        string? systemPrompt = null,
        string? threadId = null,
        CancellationToken ct = default);
}

public sealed class ChatAnswerResult
{
    public string Response { get; set; } = string.Empty;
    public string ThreadId { get; set; } = string.Empty;
}

public sealed class ChatServiceUserException(string message, int statusCode = StatusCodes.Status429TooManyRequests)
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>
/// Calls an Azure AI Foundry "Persistent Agent" (configured in Foundry with Knowledge Base / tools).
/// The retrieval (KB / AI Search / Blob, etc.) is performed by the agent based on its configuration in Foundry.
/// </summary>
public sealed class AzureOpenAiChatService : IAzureOpenAiChatService
{
    private readonly PersistentAgentsClient _client;
    private readonly string _agentId;
    private static readonly TimeSpan ActiveRunPollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ActiveRunWaitBudget = TimeSpan.FromSeconds(10);

    public AzureOpenAiChatService(IConfiguration config)
    {
        var projectEndpoint = (config["AzureAIFoundry:ProjectEndpoint"]
            ?? throw new InvalidOperationException("Missing config: AzureAIFoundry:ProjectEndpoint")).Trim();

        _agentId = (config["Agents:AgentId"]
            ?? throw new InvalidOperationException("Missing config: Agents:AgentId")).Trim();

        var clientId = (config["AzureAIFoundry:ClientId"]
                       ?? config["AzureAIFoundry__ClientId"]
                       ?? Environment.GetEnvironmentVariable("AzureAIFoundry__ClientId"))?.Trim();

        // Prefer the app's managed identity in Azure. Fall back to developer credentials locally.
        TokenCredential credential = new ChainedTokenCredential(
            string.IsNullOrWhiteSpace(clientId)
                ? new ManagedIdentityCredential()
                : new ManagedIdentityCredential(clientId),
            new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ExcludeManagedIdentityCredential = true
            }));

        _client = new PersistentAgentsClient(projectEndpoint, credential);
    }

    public async Task<ChatAnswerResult> AskAsync(
        string userPrompt,
        string? systemPrompt = null,
        string? threadId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userPrompt))
            throw new ArgumentException("User prompt is required.", nameof(userPrompt));

        // Reuse an existing thread when possible, but recover if Foundry has discarded it.
        var effectiveThreadId = threadId;
        if (string.IsNullOrWhiteSpace(effectiveThreadId))
            effectiveThreadId = await CreateThreadAsync(ct);

        effectiveThreadId = await AddUserMessageAsync(effectiveThreadId, userPrompt, ct);

        // 3) Run the agent
        ThreadRun run;
        try
        {
            run = await _client.Runs.CreateRunAsync(
                threadId: effectiveThreadId,
                _agentId,
                additionalInstructions: string.IsNullOrWhiteSpace(systemPrompt) ? null : systemPrompt,
                cancellationToken: ct
            );
        }
        catch (RequestFailedException ex) when (IsQuotaLimitException(ex))
        {
            throw new ChatServiceUserException(
                "Chat is temporarily unavailable because the AI usage limit has been reached. Please wait a minute and try again.");
        }

        // 4) Poll until terminal
        run = await WaitForRunCompletionAsync(effectiveThreadId, run, ct);

        if (run.Status != RunStatus.Completed)
        {
            var err = run.LastError?.Message ?? "Run did not complete successfully.";
            throw new InvalidOperationException($"Agent run failed: {run.Status}. {err}");
        }

        // 5) Get messages and return the last agent text response
        string? lastAgentText = null;

        await foreach (PersistentThreadMessage msg in _client.Messages.GetMessagesAsync(
                           threadId: effectiveThreadId,
                           order: ListSortOrder.Ascending,
                           cancellationToken: ct))
        {
            if (msg.Role != MessageRole.Agent)
                continue;

            var sb = new StringBuilder();
            foreach (MessageContent item in msg.ContentItems)
            {
                if (item is MessageTextContent text)
                    sb.Append(text.Text);
            }

            var combined = sb.ToString();
            if (!string.IsNullOrWhiteSpace(combined))
                lastAgentText = combined;
        }

        return new ChatAnswerResult
        {
            Response = lastAgentText ?? "(No agent text response returned.)",
            ThreadId = effectiveThreadId
        };
    }

    private async Task<string> AddUserMessageAsync(string threadId, string userPrompt, CancellationToken ct)
    {
        try
        {
            await _client.Messages.CreateMessageAsync(
                threadId: threadId,
                role: MessageRole.User,
                content: userPrompt,
                cancellationToken: ct);

            return threadId;
        }
        catch (RequestFailedException ex) when (ex.Status == 404 && ex.Message.Contains("No thread found", StringComparison.OrdinalIgnoreCase))
        {
            var newThreadId = await CreateThreadAsync(ct);

            await _client.Messages.CreateMessageAsync(
                threadId: newThreadId,
                role: MessageRole.User,
                content: userPrompt,
                cancellationToken: ct);

            return newThreadId;
        }
        catch (RequestFailedException ex) when (ex.Status == 400 && TryExtractActiveRunId(ex.Message, out var activeRunId))
        {
            var activeRun = (await _client.Runs.GetRunAsync(threadId, activeRunId, ct)).Value;
            activeRun = await WaitForRunCompletionAsync(threadId, activeRun, ct, ActiveRunWaitBudget);

            if (activeRun.Status == RunStatus.Queued || activeRun.Status == RunStatus.InProgress)
            {
                var newThreadId = await CreateThreadAsync(ct);

                await _client.Messages.CreateMessageAsync(
                    threadId: newThreadId,
                    role: MessageRole.User,
                    content: userPrompt,
                    cancellationToken: ct);

                return newThreadId;
            }

            await _client.Messages.CreateMessageAsync(
                threadId: threadId,
                role: MessageRole.User,
                content: userPrompt,
                cancellationToken: ct);

            return threadId;
        }
    }

    private async Task<string> CreateThreadAsync(CancellationToken ct)
    {
        PersistentAgentThread newThread = await _client.Threads.CreateThreadAsync(cancellationToken: ct);
        return newThread.Id;
    }

    private async Task<ThreadRun> WaitForRunCompletionAsync(
        string threadId,
        ThreadRun run,
        CancellationToken ct,
        TimeSpan? maxWait = null)
    {
        var startedAt = DateTimeOffset.UtcNow;

        while (run.Status == RunStatus.Queued || run.Status == RunStatus.InProgress)
        {
            if (maxWait is { } budget && DateTimeOffset.UtcNow - startedAt >= budget)
                return run;

            await Task.Delay(ActiveRunPollInterval, ct);
            run = await _client.Runs.GetRunAsync(threadId, run.Id, ct);
        }

        return run;
    }

    private static bool TryExtractActiveRunId(string message, out string runId)
    {
        const string marker = "run ";
        runId = string.Empty;

        var markerIndex = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
            return false;

        var start = markerIndex + marker.Length;
        var end = message.IndexOf(' ', start);
        if (end < 0)
            end = message.Length;

        var candidate = message[start..end].Trim(' ', '.', ',', '\'', '"');
        if (!candidate.StartsWith("run_", StringComparison.OrdinalIgnoreCase))
            return false;

        runId = candidate;
        return true;
    }

    private static bool IsQuotaLimitException(RequestFailedException ex)
    {
        return ex.Status == 429
               || ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
               || ex.Message.Contains("quota", StringComparison.OrdinalIgnoreCase)
               || ex.Message.Contains("retry after", StringComparison.OrdinalIgnoreCase);
    }
}
