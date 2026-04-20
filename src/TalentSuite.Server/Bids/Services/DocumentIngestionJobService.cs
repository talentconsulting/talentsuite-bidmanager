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

public sealed class DocumentIngestionJobService : IDocumentIngestionJobService
{
    private static readonly TimeSpan StreamPollInterval = TimeSpan.FromMilliseconds(200);
    private readonly ConcurrentDictionary<string, IngestionJobState> _jobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DocumentIngestionJobService> _logger;

    public DocumentIngestionJobService(
        IServiceScopeFactory scopeFactory,
        ILogger<DocumentIngestionJobService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
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

        PersistJobStateAsync(jobState, cancellationToken).GetAwaiter().GetResult();

        _ = Task.Run(() => ProcessJobAsync(jobId, jobState, fileBytes, jobState.FileName, stage, cancellationToken), CancellationToken.None);

        return jobId;
    }

    public Task<List<DocumentIngestionJobStatusResponse>> ListJobsAsync(string ownerUserKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerUserKey);
        cancellationToken.ThrowIfCancellationRequested();

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IManageBids>();
        var jobs = repository.GetDocumentIngestionJobsForUser(ownerUserKey, cancellationToken)
            .GetAwaiter()
            .GetResult()
            .Select(ToResponse)
            .ToList();

        return Task.FromResult(jobs);
    }

    public Task<DocumentIngestionJobStatusResponse?> GetJobAsync(string jobId, string ownerUserKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerUserKey);
        cancellationToken.ThrowIfCancellationRequested();

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IManageBids>();
        var job = repository.GetDocumentIngestionJob(jobId, cancellationToken)
            .GetAwaiter()
            .GetResult();
        if (job is null)
            return Task.FromResult<DocumentIngestionJobStatusResponse?>(null);

        if (!string.Equals(job.OwnerUserKey, ownerUserKey, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<DocumentIngestionJobStatusResponse?>(null);

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
                cancellationToken);

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
            _logger.LogError(ex, "Document ingestion job {JobId} failed.", jobId);
            Publish(jobState, new DocumentIngestionJobEventResponse
            {
                Status = "failed",
                Message = ex.Message,
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
