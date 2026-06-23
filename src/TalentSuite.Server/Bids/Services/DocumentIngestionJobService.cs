using System.Collections.Concurrent;
using System.Diagnostics;
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
    string StartJob(
        string ownerUserKey,
        byte[] fileBytes,
        string fileName,
        BidStage stage,
        CancellationToken cancellationToken = default);
    IAsyncEnumerable<DocumentIngestionJobEventResponse> StreamJobAsync(string jobId, CancellationToken cancellationToken = default);
    Task<List<DocumentIngestionJobStatusResponse>> ListJobsAsync(string ownerUserKey, CancellationToken cancellationToken = default);
    Task<DocumentIngestionJobStatusResponse?> GetJobAsync(string jobId, string ownerUserKey, CancellationToken cancellationToken = default);
}

public sealed partial class DocumentIngestionJobService : IDocumentIngestionJobService
{
    private static readonly ActivitySource IngestionSource = new("TalentSuite.DocumentIngestion");
    private static readonly TimeSpan StreamPollInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan DefaultJobTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DefaultAbandonedJobThreshold = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, IngestionJobState> _jobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DocumentIngestionJobService> _logger;
    private readonly TalentSuiteMetrics _metrics;
    private readonly TimeSpan _jobTimeout;
    private readonly TimeSpan _abandonedJobThreshold;

    public DocumentIngestionJobService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<DocumentIngestionJobService> logger,
        TalentSuiteMetrics metrics)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _metrics = metrics;
        _jobTimeout = GetConfiguredDuration(configuration, "DocumentIngestion:JobTimeout", DefaultJobTimeout);
        _abandonedJobThreshold = GetConfiguredDuration(configuration, "DocumentIngestion:AbandonedJobThreshold", DefaultAbandonedJobThreshold);

        if (_abandonedJobThreshold < _jobTimeout)
            _abandonedJobThreshold = _jobTimeout;
    }

    public string StartJob(
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

        _metrics.JobStarted();

        PersistJobStateAsync(jobState, cancellationToken).GetAwaiter().GetResult();

        // The background job must outlive the request that created it.
        _ = Task.Run(
            () => ProcessJobAsync(jobId, jobState, fileBytes, jobState.FileName, stage, CancellationToken.None),
            CancellationToken.None);

        return jobId;
    }

    public Task<List<DocumentIngestionJobStatusResponse>> ListJobsAsync(string ownerUserKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerUserKey);
        cancellationToken.ThrowIfCancellationRequested();

        var liveJobs = _jobs.Values
            .Where(job => string.Equals(job.OwnerUserKey, ownerUserKey, StringComparison.OrdinalIgnoreCase))
            .Select(ToResponse)
            .ToDictionary(job => job.JobId, StringComparer.OrdinalIgnoreCase);

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IManageBids>();
        var jobs = repository.GetDocumentIngestionJobsForUser(ownerUserKey, cancellationToken)
            .GetAwaiter()
            .GetResult()
            .Select(job => TryMarkStoredJobAsAbandonedAsync(job, repository, cancellationToken).GetAwaiter().GetResult())
            .Select(ToResponse)
            .Where(job => !liveJobs.ContainsKey(job.JobId))
            .Concat(liveJobs.Values)
            .OrderByDescending(job => job.CreatedAtUtc)
            .ToList();

        return Task.FromResult(jobs);
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
        using var activity = IngestionSource.StartActivity("DocumentIngestion.Process", ActivityKind.Internal);
        activity?.SetTag("job.id", jobId);
        activity?.SetTag("job.filename", fileName);
        activity?.SetTag("job.stage", stage.ToString());
        var sw = Stopwatch.StartNew();
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

            activity?.AddEvent(new ActivityEvent("ExtractionStarted"));
            var parsed = await ingestionService.ExtractDocumentAsync(
                stream,
                fileName,
                stage,
                progress,
                timeoutCts.Token);
            activity?.AddEvent(new ActivityEvent("ExtractionCompleted"));

            var response = parsed is null ? null : mapper.ToResponse(parsed);
            Publish(jobState, new DocumentIngestionJobEventResponse
            {
                Status = "completed",
                Message = "Document ingestion completed.",
                IsComplete = true,
                Result = response
            });
            await PersistJobStateAsync(jobState, cancellationToken);
            activity?.SetStatus(ActivityStatusCode.Ok);
            _metrics.JobFinished(sw.Elapsed.TotalSeconds, succeeded: true);
            LogJobCompleted(jobId, fileName, sw.Elapsed.TotalSeconds);
        }
        catch (Exception ex)
        {
            var message = ex is OperationCanceledException && cancellationToken.IsCancellationRequested is false
                ? $"Document ingestion exceeded the {_jobTimeout.TotalMinutes:0} minute time limit. Please retry with a smaller document or contact support if the issue persists."
                : ex.Message;

            LogJobFailed(ex, jobId, fileName);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
            {
                ["exception.type"] = ex.GetType().FullName,
                ["exception.message"] = ex.Message,
                ["exception.stacktrace"] = ex.ToString()
            }));
            Publish(jobState, new DocumentIngestionJobEventResponse
            {
                Status = "failed",
                Message = message,
                IsError = true
            });
            await PersistJobStateAsync(jobState, cancellationToken);
            _metrics.JobFinished(sw.Elapsed.TotalSeconds, succeeded: false);
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
            LogPersistFailed(ex, jobState.JobId);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Document ingestion job {JobId} completed for file '{FileName}' in {DurationSeconds:0.##}s")]
    private partial void LogJobCompleted(string jobId, string fileName, double durationSeconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "Document ingestion job {JobId} failed for file '{FileName}'")]
    private partial void LogJobFailed(Exception exception, string jobId, string fileName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to persist document ingestion job {JobId}")]
    private partial void LogPersistFailed(Exception exception, string jobId);

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
