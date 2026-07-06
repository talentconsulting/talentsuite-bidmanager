using System.Text;
using System.ClientModel;
using Azure;
using Azure.AI.Agents.Persistent;
using Azure.Core;
using Azure.Identity;
using TalentSuite.Shared.Bids.Ai;
using System.Collections.Concurrent;

namespace TalentSuite.Server.Bids.Services;

public interface IAzureOpenAiChatService
{
    Task<ChatAnswerResult> AskAsync(
        string userPrompt,
        string? systemPrompt = null,
        string? threadId = null,
        CancellationToken ct = default);

    IAsyncEnumerable<ChatStreamUpdate> StreamAsync(
        string userPrompt,
        string? systemPrompt = null,
        string? threadId = null,
        CancellationToken ct = default);
}

public sealed class ChatAnswerResult
{
    public string Response { get; set; } = string.Empty;
    public string ThreadId { get; set; } = string.Empty;
    public List<ChatSourceReferenceResponse> Sources { get; set; } = new();
    public bool UsedSourcesOutsideBidLibrary { get; set; }
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
    private readonly bool _isDevelopment;
    private readonly ConcurrentDictionary<string, string> _fileNameCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan ActiveRunPollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ActiveRunWaitBudget = TimeSpan.FromSeconds(10);
    // Upper bound for any run poll so a stuck Foundry run cannot hang a request forever.
    private static readonly TimeSpan DefaultRunWaitBudget = TimeSpan.FromMinutes(3);

    public AzureOpenAiChatService(IConfiguration config, IWebHostEnvironment environment)
    {
        var projectEndpoint = (config["AzureAIFoundry:ProjectEndpoint"]
            ?? throw new InvalidOperationException("Missing config: AzureAIFoundry:ProjectEndpoint")).Trim();

        _agentId = (config["Agents:AgentId"]
            ?? throw new InvalidOperationException("Missing config: Agents:AgentId")).Trim();

        var clientId = (config["AzureAIFoundry:ClientId"]
                       ?? config["AzureAIFoundry__ClientId"]
                       ?? Environment.GetEnvironmentVariable("AzureAIFoundry__ClientId"))?.Trim();

        _isDevelopment = environment.IsDevelopment();

        TokenCredential credential = _isDevelopment
            ? new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ExcludeManagedIdentityCredential = true
            })
            : string.IsNullOrWhiteSpace(clientId)
                ? new ManagedIdentityCredential()
                : new ManagedIdentityCredential(clientId);

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
            if (err.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
                || err.Contains("quota", StringComparison.OrdinalIgnoreCase)
                || err.Contains("retry after", StringComparison.OrdinalIgnoreCase))
            {
                throw new ChatServiceUserException(
                    "Chat is temporarily unavailable because the AI usage limit has been reached. Please wait a minute and try again.");
            }

            throw new InvalidOperationException($"Agent run failed: {run.Status}. {err}");
        }

        // 5) Get messages and return the last agent text response
        PersistentThreadMessage? lastAgentMessage = null;

        await foreach (PersistentThreadMessage msg in _client.Messages.GetMessagesAsync(
                           threadId: effectiveThreadId,
                           order: ListSortOrder.Ascending,
                           cancellationToken: ct))
        {
            if (msg.Role != MessageRole.Agent)
                continue;

            if (!string.IsNullOrWhiteSpace(ExtractMessageText(msg)))
                lastAgentMessage = msg;
        }

        var responseText = lastAgentMessage is null
            ? "(No agent text response returned.)"
            : ExtractMessageText(lastAgentMessage);
        var fileNameMap = await BuildRunFileNameMapAsync(effectiveThreadId, run.Id, ct);
        var provenance = lastAgentMessage is null
            ? ChatMessageProvenance.Empty
            : ExtractProvenance(lastAgentMessage, fileNameMap);

        return new ChatAnswerResult
        {
            Response = responseText,
            ThreadId = effectiveThreadId,
            Sources = provenance.Sources,
            UsedSourcesOutsideBidLibrary = provenance.UsedSourcesOutsideBidLibrary
        };
    }

    public async IAsyncEnumerable<ChatStreamUpdate> StreamAsync(
        string userPrompt,
        string? systemPrompt = null,
        string? threadId = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userPrompt))
            throw new ArgumentException("User prompt is required.", nameof(userPrompt));

        var effectiveThreadId = threadId;
        if (string.IsNullOrWhiteSpace(effectiveThreadId))
            effectiveThreadId = await CreateThreadAsync(ct);

        effectiveThreadId = await AddUserMessageAsync(effectiveThreadId, userPrompt, ct);

        yield return new ChatStreamUpdate
        {
            Type = "thread",
            ThreadId = effectiveThreadId
        };

        AsyncCollectionResult<StreamingUpdate> stream;
        try
        {
            stream = _client.Runs.CreateRunStreamingAsync(
                effectiveThreadId,
                _agentId,
                new CreateRunStreamingOptions
                {
                    AdditionalInstructions = string.IsNullOrWhiteSpace(systemPrompt) ? null : systemPrompt
                },
                ct);
        }
        catch (RequestFailedException ex) when (IsQuotaLimitException(ex))
        {
            throw new ChatServiceUserException(
                "Chat is temporarily unavailable because the AI usage limit has been reached. Please wait a minute and try again.");
        }

        ThreadRun? streamRun = null;
        List<ToolOutput> toolOutputs = [];

        do
        {
            toolOutputs.Clear();

            await foreach (var streamingUpdate in stream)
            {
                if (streamingUpdate.UpdateKind == StreamingUpdateReason.RunCreated && streamingUpdate is RunUpdate runCreated)
                {
                    streamRun = runCreated.Value;
                    continue;
                }

                if (streamingUpdate is RequiredActionUpdate requiredActionUpdate)
                {
                    streamRun = requiredActionUpdate.Value;
                    throw new InvalidOperationException(
                        $"Streaming chat does not support required tool action '{requiredActionUpdate.FunctionName}'.");
                }

                if (streamingUpdate is MessageContentUpdate contentUpdate && !string.IsNullOrWhiteSpace(contentUpdate.Text))
                {
                    yield return new ChatStreamUpdate
                    {
                        Type = "delta",
                        ThreadId = effectiveThreadId,
                        Content = contentUpdate.Text
                    };
                    continue;
                }

                if (streamingUpdate.UpdateKind == StreamingUpdateReason.RunCompleted)
                {
                    var completedMessage = await GetLastAgentMessageAsync(effectiveThreadId, ct);
                    var fileNameMap = streamRun is null
                        ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        : await BuildRunFileNameMapAsync(effectiveThreadId, streamRun.Id, ct);
                    var provenance = completedMessage is null
                        ? ChatMessageProvenance.Empty
                        : ExtractProvenance(completedMessage, fileNameMap);

                    yield return new ChatStreamUpdate
                    {
                        Type = "completed",
                        ThreadId = effectiveThreadId,
                        Sources = provenance.Sources,
                        UsedSourcesOutsideBidLibrary = provenance.UsedSourcesOutsideBidLibrary
                    };
                    yield break;
                }

                if (streamingUpdate.UpdateKind == StreamingUpdateReason.Error && streamingUpdate is RunUpdate errorStep)
                {
                    var err = errorStep.Value.LastError?.Message ?? "Chat streaming failed.";
                    if (err.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
                        || err.Contains("quota", StringComparison.OrdinalIgnoreCase)
                        || err.Contains("retry after", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new ChatServiceUserException(
                            "Chat is temporarily unavailable because the AI usage limit has been reached. Please wait a minute and try again.");
                    }

                    throw new InvalidOperationException(err);
                }
            }

            if (toolOutputs.Count > 0)
                stream = _client.Runs.SubmitToolOutputsToStreamAsync(streamRun!, toolOutputs, ct);
        }
        while (toolOutputs.Count > 0);
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
        var waitBudget = maxWait ?? DefaultRunWaitBudget;
        var startedAt = DateTimeOffset.UtcNow;

        while (run.Status == RunStatus.Queued || run.Status == RunStatus.InProgress)
        {
            if (DateTimeOffset.UtcNow - startedAt >= waitBudget)
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

    private static string ExtractMessageText(PersistentThreadMessage message)
    {
        var sb = new StringBuilder();
        foreach (var item in message.ContentItems)
        {
            if (item is MessageTextContent text)
                sb.Append(text.Text);
        }

        return sb.ToString();
    }

    private async Task<PersistentThreadMessage?> GetLastAgentMessageAsync(string threadId, CancellationToken ct)
    {
        await foreach (var msg in _client.Messages.GetMessagesAsync(
                           threadId: threadId,
                           order: ListSortOrder.Descending,
                           cancellationToken: ct))
        {
            if (msg.Role == MessageRole.Agent && !string.IsNullOrWhiteSpace(ExtractMessageText(msg)))
                return msg;
        }

        return null;
    }

    private ChatMessageProvenance ExtractProvenance(
        PersistentThreadMessage message,
        IReadOnlyDictionary<string, string> fileNameMap)
    {
        var sources = new List<ChatSourceReferenceResponse>();
        var usedSourcesOutsideBidLibrary = false;

        foreach (var item in message.ContentItems)
        {
            if (item is not MessageTextContent textContent)
                continue;

            foreach (var annotation in textContent.Annotations)
            {
                switch (annotation)
                {
                    case MessageTextFileCitationAnnotation fileCitation:
                    {
                        var fileName = ResolveFileName(fileCitation.FileId, fileNameMap);
                        var isFromBidLibrary = !string.IsNullOrWhiteSpace(fileName);
                        usedSourcesOutsideBidLibrary |= !isFromBidLibrary;
                        sources.Add(new ChatSourceReferenceResponse
                        {
                            Kind = "file",
                            FileId = fileCitation.FileId,
                            FileName = fileName ?? fileCitation.FileId,
                            Quote = fileCitation.Quote,
                            IsFromBidLibrary = isFromBidLibrary
                        });
                        break;
                    }
                    case MessageTextFilePathAnnotation filePath:
                    {
                        var fileName = ResolveFileName(filePath.FileId, fileNameMap);
                        var isFromBidLibrary = !string.IsNullOrWhiteSpace(fileName);
                        usedSourcesOutsideBidLibrary |= !isFromBidLibrary;
                        sources.Add(new ChatSourceReferenceResponse
                        {
                            Kind = "file",
                            FileId = filePath.FileId,
                            FileName = fileName ?? filePath.FileId,
                            IsFromBidLibrary = isFromBidLibrary
                        });
                        break;
                    }
                    case MessageTextUriCitationAnnotation uriCitation:
                        usedSourcesOutsideBidLibrary = true;
                        sources.Add(new ChatSourceReferenceResponse
                        {
                            Kind = "uri",
                            Uri = uriCitation.UriCitation.Uri,
                            Title = uriCitation.UriCitation.Title,
                            IsFromBidLibrary = false
                        });
                        break;
                }
            }
        }

        return new ChatMessageProvenance(
            sources
                .GroupBy(source => $"{source.Kind}|{source.FileId}|{source.FileName}|{source.Uri}|{source.Title}|{source.Quote}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList(),
            usedSourcesOutsideBidLibrary);
    }

    private string? ResolveFileName(string? fileId, IReadOnlyDictionary<string, string> fileNameMap)
    {
        if (string.IsNullOrWhiteSpace(fileId))
            return null;

        if (_fileNameCache.TryGetValue(fileId, out var cachedFileName))
            return cachedFileName;

        if (!fileNameMap.TryGetValue(fileId, out var fileName) || string.IsNullOrWhiteSpace(fileName))
            return null;

        _fileNameCache[fileId] = fileName;
        return fileName;
    }

    private async Task<Dictionary<string, string>> BuildRunFileNameMapAsync(string threadId, string runId, CancellationToken ct)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        await foreach (var step in _client.Runs.GetRunStepsAsync(threadId, runId, null, null, null, null, null, ct))
        {
            if (step.StepDetails is not RunStepToolCallDetails toolCallDetails)
                continue;

            foreach (var toolCall in toolCallDetails.ToolCalls)
            {
                if (toolCall is not RunStepFileSearchToolCall fileSearchToolCall)
                    continue;

                foreach (var result in fileSearchToolCall.FileSearch.Results)
                {
                    if (!string.IsNullOrWhiteSpace(result.FileId) && !string.IsNullOrWhiteSpace(result.FileName))
                        map[result.FileId] = result.FileName;
                }
            }
        }

        return map;
    }

    private sealed record ChatMessageProvenance(
        List<ChatSourceReferenceResponse> Sources,
        bool UsedSourcesOutsideBidLibrary)
    {
        public static ChatMessageProvenance Empty { get; } = new([], false);
    }
}
