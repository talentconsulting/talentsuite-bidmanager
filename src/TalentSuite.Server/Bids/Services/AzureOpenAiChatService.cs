using System.Text;
using Azure.AI.Agents.Persistent;
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

/// <summary>
/// Calls an Azure AI Foundry "Persistent Agent" (configured in Foundry with Knowledge Base / tools).
/// The retrieval (KB / AI Search / Blob, etc.) is performed by the agent based on its configuration in Foundry.
/// </summary>
public sealed class AzureOpenAiChatService : IAzureOpenAiChatService
{
    private readonly PersistentAgentsClient _client;
    private readonly string _agentId;

    public AzureOpenAiChatService(IConfiguration config)
    {
        var projectEndpoint = config["AzureAIFoundry:ProjectEndpoint"]
            ?? throw new InvalidOperationException("Missing config: AzureAIFoundry:ProjectEndpoint");

        _agentId = config["Agents:AgentId"]
            ?? throw new InvalidOperationException("Missing config: Agents:AgentId");

        // Keyless auth via Entra ID
        _client = new PersistentAgentsClient(projectEndpoint, new DefaultAzureCredential());
    }

    public async Task<ChatAnswerResult> AskAsync(
        string userPrompt,
        string? systemPrompt = null,
        string? threadId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userPrompt))
            throw new ArgumentException("User prompt is required.", nameof(userPrompt));

        // 1) Reuse existing thread when provided, otherwise create a new one
        var effectiveThreadId = threadId;
        if (string.IsNullOrWhiteSpace(effectiveThreadId))
        {
            PersistentAgentThread newThread = await _client.Threads.CreateThreadAsync(cancellationToken: ct);
            effectiveThreadId = newThread.Id;
        }

        // 2) Add user message
        await _client.Messages.CreateMessageAsync(
            threadId: effectiveThreadId,
            role: MessageRole.User,
            content: userPrompt,
            cancellationToken: ct
        );

        // 3) Run the agent
        ThreadRun run = await _client.Runs.CreateRunAsync(
            threadId: effectiveThreadId,
            _agentId,
            additionalInstructions: string.IsNullOrWhiteSpace(systemPrompt) ? null : systemPrompt,
            cancellationToken: ct
        );

        // 4) Poll until terminal
        while (run.Status == RunStatus.Queued || run.Status == RunStatus.InProgress)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
            run = await _client.Runs.GetRunAsync(effectiveThreadId, run.Id, ct);
        }

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
}
