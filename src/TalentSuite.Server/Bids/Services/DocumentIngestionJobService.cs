using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TalentSuite.Server.Bids.Data;
using TalentSuite.Server.Bids.Data.Models;
using TalentSuite.Server.Bids.Mappers;
using TalentSuite.Shared;
using TalentSuite.Shared.Bids;

namespace TalentSuite.Server.Bids.Services;

public interface IDocumentIngestionJobService
{
    Task<string> StartJob(
        string ownerUserKey,
        byte[] fileBytes,
        string fileName,
        BidStage stage,
        CancellationToken cancellationToken = default);
    IAsyncEnumerable<DocumentIngestionJobEventResponse> StreamJobAsync(string jobId, CancellationToken cancellationToken = default);
    Task<List<DocumentIngestionJobStatusResponse>> ListJobsAsync(string ownerUserKey, CancellationToken cancellationToken = default);
    Task<DocumentIngestionJobStatusResponse?> GetJobAsync(string jobId, string ownerUserKey, CancellationToken cancellationToken = default);
}

public sealed class DocumentIngestionJobService : IDocumentIngestionJobService
{
    private static readonly TimeSpan StreamPollInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan DefaultJobTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DefaultAbandonedJobThreshold = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, IngestionJobState> _jobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DocumentIngestionJobService> _logger;
    private readonly TimeSpan _jobTimeout;
    private readonly TimeSpan _abandonedJobThreshold;

    public DocumentIngestionJobService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<DocumentIngestionJobService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _jobTimeout = GetConfiguredDuration(configuration, "DocumentIngestion:JobTimeout", DefaultJobTimeout);
        _abandonedJobThreshold = GetConfiguredDuration(configuration, "DocumentIngestion:AbandonedJobThreshold", DefaultAbandonedJobThreshold);

        if (_abandonedJobThreshold < _jobTimeout)
            _abandonedJobThreshold = _jobTimeout;
    }

    public async Task<string> StartJob(
        string ownerUserKey,
        byte[] fileBytes,
        string fileName,
        BidStage stage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerUserKey);

        var jobId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var jobState = new IngestionJobState
        {
            JobId = jobId,
            OwnerUserKey = ownerUserKey,
            FileName = fileName ?? string.Empty,
            Stage = stage,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        Publish(jobState, new DocumentIngestionJobEventResponse
        {
            Status = "queued",
            Message = "Document received. Waiting to start ingestion."
        });

        if (!_jobs.TryAdd(jobId, jobState))
            throw new InvalidOperationException("Could not create a new ingestion job.");

        await PersistJobStateAsync(jobState, cancellationToken);

        // The background job must outlive the request that created it.
        _ = Task.Run(
            () => ProcessJobAsync(jobId, jobState, fileBytes, jobState.FileName, stage, CancellationToken.None),
            CancellationToken.None);

        return jobId;
    }

    public async Task<List<DocumentIngestionJobStatusResponse>> ListJobsAsync(string ownerUserKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerUserKey);
        cancellationToken.ThrowIfCancellationRequested();

        var liveJobs = _jobs.Values
            .Where(job => string.Equals(job.OwnerUserKey, ownerUserKey, StringComparison.OrdinalIgnoreCase))
            .Select(ToResponse)
            .ToDictionary(job => job.JobId, StringComparer.OrdinalIgnoreCase);

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IManageBids>();
        var storedJobs = await repository.GetDocumentIngestionJobsForUser(ownerUserKey, cancellationToken);

        var storedResponses = new List<DocumentIngestionJobStatusResponse>(storedJobs.Count);
        foreach (var job in storedJobs)
        {
            var updatedJob = await TryMarkStoredJobAsAbandonedAsync(job, repository, cancellationToken);
            var response = ToResponse(updatedJob);
            if (!liveJobs.ContainsKey(response.JobId))
                storedResponses.Add(response);
        }

        return storedResponses
            .Concat(liveJobs.Values)
            .OrderByDescending(job => job.CreatedAtUtc)
            .ToList();
    }

    public Task<DocumentIngestionJobStatusResponse?> GetJobAsync(string jobId, string ownerUserKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerUserKey);
        cancellationToken.ThrowIfCancellationRequested();

        if (_jobs.TryGetValue(jobId, out var liveJob)
            && string.Equals(liveJob.OwnerUserKey, ownerUserKey, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<DocumentIngestionJobStatusResponse?>(ToResponse(liveJob));
        }

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IManageBids>();
        var job = repository.GetDocumentIngestionJob(jobId, cancellationToken)
            .GetAwaiter()
            .GetResult();
        if (job is null)
            return Task.FromResult<DocumentIngestionJobStatusResponse?>(null);

        if (!string.Equals(job.OwnerUserKey, ownerUserKey, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<DocumentIngestionJobStatusResponse?>(null);

        job = TryMarkStoredJobAsAbandonedAsync(job, repository, cancellationToken)
            .GetAwaiter()
            .GetResult();

        return Task.FromResult<DocumentIngestionJobStatusResponse?>(ToResponse(job));
    }

    public async IAsyncEnumerable<DocumentIngestionJobEventResponse> StreamJobAsync(
        string jobId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_jobs.TryGetValue(jobId, out var jobState))
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IManageBids>();
            var storedJob = await repository.GetDocumentIngestionJob(jobId, cancellationToken);
            if (storedJob is null)
                throw new KeyNotFoundException($"Ingestion job '{jobId}' was not found.");

            storedJob = await TryMarkStoredJobAsAbandonedAsync(storedJob, repository, cancellationToken);
            yield return ToTerminalEvent(storedJob);
            yield break;
        }

        var emittedCount = 0;
        while (true)
        {
            List<DocumentIngestionJobEventResponse> pendingEntries;
            var isCompleted = false;

            lock (jobState.SyncRoot)
            {
                pendingEntries = jobState.History.Skip(emittedCount).ToList();
                emittedCount = jobState.History.Count;
                isCompleted = jobState.IsCompleted;
            }

            foreach (var entry in pendingEntries)
            {
                yield return entry;
                if (entry.IsComplete || entry.IsError)
                    yield break;
            }

            if (isCompleted)
                yield break;

            await Task.Delay(StreamPollInterval, cancellationToken);
        }
    }

    private async Task ProcessJobAsync(
        string jobId,
        IngestionJobState jobState,
        byte[] fileBytes,
        string fileName,
        BidStage stage,
        CancellationToken cancellationToken)
    {
        try
        {
            Publish(jobState, new DocumentIngestionJobEventResponse
            {
                Status = "started",
                Message = "Starting document ingestion."
            });
            await PersistJobStateAsync(jobState, cancellationToken);

            using var scope = _scopeFactory.CreateScope();
            var ingestionService = scope.ServiceProvider.GetRequiredService<IDocumentIngestionservice>();
            var mapper = scope.ServiceProvider.GetRequiredService<BidMapper>();
            using var stream = new MemoryStream(fileBytes, writable: false);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_jobTimeout);

            var progress = new Progress<DocumentIngestionProgressUpdate>(update =>
            {
                Publish(jobState, new DocumentIngestionJobEventResponse
                {
                    Status = update.Status,
                    Message = update.Message
                });
            });

            var parsed = await ingestionService.ExtractDocumentAsync(
                stream,
                fileName,
                stage,
                progress,
                timeoutCts.Token);

            var response = parsed is null ? null : mapper.ToResponse(parsed);
            Publish(jobState, new DocumentIngestionJobEventResponse
            {
                Status = "completed",
                Message = "Document ingestion completed.",
                IsComplete = true,
                Result = response
            });
            await PersistJobStateAsync(jobState, cancellationToken);
        }
        catch (Exception ex)
        {
            var message = ex is OperationCanceledException && cancellationToken.IsCancellationRequested is false
                ? $"Document ingestion exceeded the {_jobTimeout.TotalMinutes:0} minute time limit. Please retry with a smaller document or contact support if the issue persists."
                : ex.Message;

            _logger.LogError(ex, "Document ingestion job {JobId} failed.", jobId);
            Publish(jobState, new DocumentIngestionJobEventResponse
            {
                Status = "failed",
                Message = message,
                IsError = true
            });
            await PersistJobStateAsync(jobState, cancellationToken);
        }
        finally
        {
        }
    }

    private void Publish(IngestionJobState jobState, DocumentIngestionJobEventResponse update)
    {
        var json = JsonSerializer.Serialize(update, SerialiserOptions.JsonOptions);
        var storedUpdate = JsonSerializer.Deserialize<DocumentIngestionJobEventResponse>(json, SerialiserOptions.JsonOptions)
                           ?? update;

        lock (jobState.SyncRoot)
        {
            jobState.History.Add(storedUpdate);
            jobState.Status = storedUpdate.Status;
            jobState.Message = storedUpdate.Message;
            jobState.Result = storedUpdate.Result;
            jobState.UpdatedAtUtc = DateTimeOffset.UtcNow;
            if (storedUpdate.IsComplete || storedUpdate.IsError)
            {
                jobState.IsCompleted = true;
                jobState.CompletedAtUtc = jobState.UpdatedAtUtc;
            }
        }

        _ = PersistJobStateFireAndForgetAsync(jobState);
    }

    private static DocumentIngestionJobStatusResponse ToResponse(IngestionJobState jobState)
    {
        lock (jobState.SyncRoot)
        {
            var lastEvent = jobState.History.LastOrDefault();
            return new DocumentIngestionJobStatusResponse
            {
                JobId = jobState.JobId,
                FileName = jobState.FileName,
                Stage = jobState.Stage,
                Status = jobState.Status,
                Message = jobState.Message,
                IsComplete = lastEvent?.IsComplete ?? false,
                IsError = lastEvent?.IsError ?? false,
                CreatedAtUtc = jobState.CreatedAtUtc,
                UpdatedAtUtc = jobState.UpdatedAtUtc,
                CompletedAtUtc = jobState.CompletedAtUtc,
                Result = jobState.Result
            };
        }
    }

    private static DocumentIngestionJobStatusResponse ToResponse(DocumentIngestionJobDataModel job)
    {
        return new DocumentIngestionJobStatusResponse
        {
            JobId = job.JobId,
            FileName = job.FileName,
            Stage = job.Stage,
            Status = job.Status,
            Message = job.Message,
            IsComplete = job.IsComplete,
            IsError = job.IsError,
            CreatedAtUtc = job.CreatedAtUtc,
            UpdatedAtUtc = job.UpdatedAtUtc,
            CompletedAtUtc = job.CompletedAtUtc,
            Result = job.Result
        };
    }

    private static DocumentIngestionJobEventResponse ToTerminalEvent(DocumentIngestionJobDataModel job)
    {
        return new DocumentIngestionJobEventResponse
        {
            Status = job.Status,
            Message = job.Message,
            IsComplete = job.IsComplete,
            IsError = job.IsError,
            Result = job.Result
        };
    }

    private DocumentIngestionJobDataModel ToDataModel(IngestionJobState jobState)
    {
        lock (jobState.SyncRoot)
        {
            var lastEvent = jobState.History.LastOrDefault();
            return new DocumentIngestionJobDataModel
            {
                JobId = jobState.JobId,
                OwnerUserKey = jobState.OwnerUserKey,
                FileName = jobState.FileName,
                Stage = jobState.Stage,
                Status = jobState.Status,
                Message = jobState.Message,
                IsComplete = lastEvent?.IsComplete ?? false,
                IsError = lastEvent?.IsError ?? false,
                CreatedAtUtc = jobState.CreatedAtUtc,
                UpdatedAtUtc = jobState.UpdatedAtUtc,
                CompletedAtUtc = jobState.CompletedAtUtc,
                Result = jobState.Result
            };
        }
    }

    private async Task PersistJobStateAsync(IngestionJobState jobState, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IManageBids>();
        await repository.SaveDocumentIngestionJob(ToDataModel(jobState), cancellationToken);
    }

    private async Task PersistJobStateFireAndForgetAsync(IngestionJobState jobState)
    {
        try
        {
            await PersistJobStateAsync(jobState, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist document ingestion job {JobId}.", jobState.JobId);
        }
    }

    private async Task<DocumentIngestionJobDataModel> TryMarkStoredJobAsAbandonedAsync(
        DocumentIngestionJobDataModel job,
        IManageBids repository,
        CancellationToken cancellationToken)
    {
        if (job.IsComplete || job.IsError)
            return job;

        if (DateTimeOffset.UtcNow - job.UpdatedAtUtc < _abandonedJobThreshold)
            return job;

        job.Status = "failed";
        job.IsError = true;
        job.Message = "Document ingestion did not finish. The background worker may have restarted or the request timed out. Please retry the upload.";
        job.UpdatedAtUtc = DateTimeOffset.UtcNow;
        job.CompletedAtUtc = job.UpdatedAtUtc;
        await repository.SaveDocumentIngestionJob(job, cancellationToken);
        return job;
    }

    private static TimeSpan GetConfiguredDuration(IConfiguration configuration, string key, TimeSpan fallback)
    {
        var value = configuration[key];
        return TimeSpan.TryParse(value, out var parsed) && parsed > TimeSpan.Zero
            ? parsed
            : fallback;
    }

    private sealed class IngestionJobState
    {
        public object SyncRoot { get; } = new();
        public string JobId { get; init; } = string.Empty;
        public string OwnerUserKey { get; init; } = string.Empty;
        public string FileName { get; init; } = string.Empty;
        public BidStage Stage { get; init; }
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTimeOffset CreatedAtUtc { get; init; }
        public DateTimeOffset UpdatedAtUtc { get; set; }
        public DateTimeOffset? CompletedAtUtc { get; set; }
        public ParsedDocumentResponse? Result { get; set; }
        public List<DocumentIngestionJobEventResponse> History { get; } = [];
        public bool IsCompleted { get; set; }
    }
}
