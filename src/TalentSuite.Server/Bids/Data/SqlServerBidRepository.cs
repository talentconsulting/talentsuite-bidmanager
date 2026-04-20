using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using TalentSuite.Server.Bids.Data.Models;
using TalentSuite.Shared.Bids;

namespace TalentSuite.Server.Bids.Data;

public sealed class SqlServerBidRepository : IManageBids
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static bool _schemaInitialized;
    private static readonly SemaphoreSlim SchemaLock = new(1, 1);

    private readonly string _connectionString;

    public SqlServerBidRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("talentconsultingdb")
                            ?? throw new InvalidOperationException(
                                "Connection string 'talentconsultingdb' was not found.");
    }

    public async Task<Guid> StoreBid(BidDataModel request, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        var guid = Guid.NewGuid();
        request.Id = guid.ToString();

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        request.Questions.ForEach(q => q.Id = Guid.NewGuid().ToString());

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dbo.Bids (Id, Payload)
            VALUES (@Id, @Payload);
            """,
            new { Id = request.Id, Payload = Serialize(request) },
            transaction: tx,
            cancellationToken: ct));

        foreach (var question in request.Questions)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO dbo.RedReviews (QuestionId, Payload)
                VALUES (@QuestionId, @Payload);
                """,
                new
                {
                    QuestionId = question.Id,
                    Payload = Serialize(new RedReviewDataModel
                    {
                        QuestionId = question.Id,
                        Comments = new List<DraftCommentDataModel>(),
                        Reviewers = new List<RedReviewReviewerDataModel>()
                    })
                },
                transaction: tx,
                cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO dbo.FinalAnswers (QuestionId, Payload)
                VALUES (@QuestionId, @Payload);
                """,
                new
                {
                    QuestionId = question.Id,
                    Payload = Serialize(new FinalAnswerDataModel
                    {
                        QuestionId = question.Id,
                        Comments = new List<DraftCommentDataModel>()
                    })
                },
                transaction: tx,
                cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
        return guid;
    }

    public async Task<BidDataModel> GetBid(string id, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        var payload = await connection.QuerySingleOrDefaultAsync<string>(
            new CommandDefinition(
                "SELECT Payload FROM dbo.Bids WHERE Id = @Id",
                new { Id = id },
                cancellationToken: ct));
        return payload is null ? null! : Deserialize<BidDataModel>(payload);
    }

    public async Task<SearchDataModel> SearchBids(int page, int pageSize, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var payloads = (await connection.QueryAsync<string>(
            new CommandDefinition(
                "SELECT Payload FROM dbo.Bids ORDER BY CreatedAtUtc DESC",
                cancellationToken: ct))).ToList();

        var bids = payloads.Select(Deserialize<BidDataModel>).ToList();
        var items = bids
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(b => new SearchItemDataModel
            {
                Id = b.Id,
                Company = b.Company ?? string.Empty,
                Summary = b.Summary ?? string.Empty,
                QuestionCount = b.Questions?.Count ?? 0,
                Status = b.Status
            })
            .ToList();

        return new SearchDataModel
        {
            CurrentPage = page,
            PageSize = pageSize,
            Items = items,
            TotalCount = bids.Count
        };
    }

    public async Task SaveDocumentIngestionJob(DocumentIngestionJobDataModel job, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        await EnsureSchemaAsync(ct);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            IF EXISTS (SELECT 1 FROM dbo.DocumentIngestionJobs WHERE JobId = @JobId)
            BEGIN
                UPDATE dbo.DocumentIngestionJobs
                SET OwnerUserKey = @OwnerUserKey,
                    FileName = @FileName,
                    Stage = @Stage,
                    Status = @Status,
                    Message = @Message,
                    IsComplete = @IsComplete,
                    IsError = @IsError,
                    UpdatedAtUtc = @UpdatedAtUtc,
                    CompletedAtUtc = @CompletedAtUtc,
                    ResultPayload = @ResultPayload
                WHERE JobId = @JobId;
            END
            ELSE
            BEGIN
                INSERT INTO dbo.DocumentIngestionJobs
                    (JobId, OwnerUserKey, FileName, Stage, Status, Message, IsComplete, IsError, CreatedAtUtc, UpdatedAtUtc, CompletedAtUtc, ResultPayload)
                VALUES
                    (@JobId, @OwnerUserKey, @FileName, @Stage, @Status, @Message, @IsComplete, @IsError, @CreatedAtUtc, @UpdatedAtUtc, @CompletedAtUtc, @ResultPayload);
            END;
            """,
            new
            {
                job.JobId,
                job.OwnerUserKey,
                job.FileName,
                Stage = job.Stage.ToString(),
                job.Status,
                job.Message,
                job.IsComplete,
                job.IsError,
                job.CreatedAtUtc,
                job.UpdatedAtUtc,
                job.CompletedAtUtc,
                ResultPayload = job.Result is null ? null : Serialize(job.Result)
            },
            cancellationToken: ct));
    }

    public async Task<DocumentIngestionJobDataModel?> GetDocumentIngestionJob(string jobId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var row = await connection.QuerySingleOrDefaultAsync<DocumentIngestionJobRow>(
            new CommandDefinition(
                """
                SELECT JobId, OwnerUserKey, FileName, Stage, Status, Message, IsComplete, IsError, CreatedAtUtc, UpdatedAtUtc, CompletedAtUtc, ResultPayload
                FROM dbo.DocumentIngestionJobs
                WHERE JobId = @JobId;
                """,
                new { JobId = jobId },
                cancellationToken: ct));

        return row is null ? null : MapDocumentIngestionJob(row);
    }

    public async Task<List<DocumentIngestionJobDataModel>> GetDocumentIngestionJobsForUser(string ownerUserKey, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var rows = await connection.QueryAsync<DocumentIngestionJobRow>(
            new CommandDefinition(
                """
                SELECT JobId, OwnerUserKey, FileName, Stage, Status, Message, IsComplete, IsError, CreatedAtUtc, UpdatedAtUtc, CompletedAtUtc, ResultPayload
                FROM dbo.DocumentIngestionJobs
                WHERE OwnerUserKey = @OwnerUserKey
                ORDER BY CreatedAtUtc DESC;
                """,
                new { OwnerUserKey = ownerUserKey },
                cancellationToken: ct));

        return rows.Select(MapDocumentIngestionJob).ToList();
    }

    public async Task<List<string>> GetBidUsers(string bidId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        return (await connection.QueryAsync<string>(new CommandDefinition(
            """
            SELECT UserId
            FROM dbo.BidUsers
            WHERE BidId = @BidId;
            """,
            new { BidId = bidId },
            cancellationToken: ct))).ToList();
    }

    public async Task AddBidUser(string bidId, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        await EnsureSchemaAsync(ct);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dbo.BidUsers (BidId, UserId)
            SELECT @BidId, @UserId
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.BidUsers WHERE BidId = @BidId AND UserId = @UserId
            );
            """,
            new { BidId = bidId, UserId = userId },
            cancellationToken: ct));
    }

    public async Task RemoveBidUser(string bidId, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        await EnsureSchemaAsync(ct);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM dbo.BidUsers
            WHERE BidId = @BidId AND UserId = @UserId;
            """,
            new { BidId = bidId, UserId = userId },
            cancellationToken: ct));
    }

    public async Task<List<BidFileDataModel>> GetBidFiles(string bidId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var rows = await connection.QueryAsync<(string Id, string BidId, string FileName, string ContentType, long SizeBytes, DateTimeOffset UploadedAtUtc)>(
            new CommandDefinition(
                """
                SELECT Id, BidId, FileName, ContentType, SizeBytes, UploadedAtUtc
                FROM dbo.BidFiles
                WHERE BidId = @BidId
                ORDER BY UploadedAtUtc DESC;
                """,
                new { BidId = bidId },
                cancellationToken: ct));

        return rows
            .Select(row => new BidFileDataModel
            {
                Id = row.Id,
                BidId = row.BidId,
                FileName = row.FileName,
                ContentType = row.ContentType,
                SizeBytes = row.SizeBytes,
                UploadedAtUtc = row.UploadedAtUtc.UtcDateTime,
                Content = Array.Empty<byte>()
            })
            .ToList();
    }

    public async Task<BidFileDataModel> AddBidFile(
        string bidId,
        string fileName,
        string contentType,
        byte[] content,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bidId))
            throw new InvalidOperationException("Invalid request, bidId is null or empty.");

        await EnsureSchemaAsync(ct);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var bidExists = await connection.QuerySingleOrDefaultAsync<int?>(
            new CommandDefinition(
                "SELECT 1 FROM dbo.Bids WHERE Id = @Id",
                new { Id = bidId },
                cancellationToken: ct));
        if (bidExists is null)
            throw new InvalidOperationException("Invalid request, bid does not exist.");

        var id = Guid.NewGuid().ToString();
        var uploadedAtUtc = DateTimeOffset.UtcNow;
        var safeContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType;
        var safeContent = content ?? Array.Empty<byte>();

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dbo.BidFiles (Id, BidId, FileName, ContentType, SizeBytes, Content, UploadedAtUtc)
            VALUES (@Id, @BidId, @FileName, @ContentType, @SizeBytes, @Content, @UploadedAtUtc);
            """,
            new
            {
                Id = id,
                BidId = bidId,
                FileName = fileName ?? string.Empty,
                ContentType = safeContentType,
                SizeBytes = safeContent.LongLength,
                Content = safeContent,
                UploadedAtUtc = uploadedAtUtc
            },
            cancellationToken: ct));

        return new BidFileDataModel
        {
            Id = id,
            BidId = bidId,
            FileName = fileName ?? string.Empty,
            ContentType = safeContentType,
            SizeBytes = safeContent.LongLength,
            UploadedAtUtc = uploadedAtUtc.UtcDateTime,
            Content = Array.Empty<byte>()
        };
    }

    public async Task<BidFileDataModel?> GetBidFile(string bidId, string fileId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var row = await connection.QuerySingleOrDefaultAsync<(string Id, string BidId, string FileName, string ContentType, long SizeBytes, byte[] Content, DateTimeOffset UploadedAtUtc)?>(
            new CommandDefinition(
                """
                SELECT Id, BidId, FileName, ContentType, SizeBytes, Content, UploadedAtUtc
                FROM dbo.BidFiles
                WHERE BidId = @BidId AND Id = @Id;
                """,
                new { BidId = bidId, Id = fileId },
                cancellationToken: ct));

        if (row is null)
            return null;

        return new BidFileDataModel
        {
            Id = row.Value.Id,
            BidId = row.Value.BidId,
            FileName = row.Value.FileName,
            ContentType = row.Value.ContentType,
            SizeBytes = row.Value.SizeBytes,
            UploadedAtUtc = row.Value.UploadedAtUtc.UtcDateTime,
            Content = row.Value.Content ?? Array.Empty<byte>()
        };
    }

    public async Task<bool> DeleteBidFile(string bidId, string fileId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM dbo.BidFiles
            WHERE BidId = @BidId AND Id = @Id;
            """,
            new { BidId = bidId, Id = fileId },
            cancellationToken: ct));

        return affected > 0;
    }

    public async Task<string?> GetChatThreadId(string bidId, string questionId, string userId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        return await connection.QuerySingleOrDefaultAsync<string>(
            new CommandDefinition(
                """
                SELECT ThreadId
                FROM dbo.ChatThreads
                WHERE BidId = @BidId
                  AND QuestionId = @QuestionId
                  AND UserId = @UserId;
                """,
                new { BidId = bidId, QuestionId = questionId, UserId = userId },
                cancellationToken: ct));
    }

    public async Task SetChatThreadId(string bidId, string questionId, string userId, string threadId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(threadId))
            return;

        await EnsureSchemaAsync(ct);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            IF EXISTS (
                SELECT 1
                FROM dbo.ChatThreads
                WHERE BidId = @BidId
                  AND QuestionId = @QuestionId
                  AND UserId = @UserId
            )
            BEGIN
                UPDATE dbo.ChatThreads
                SET ThreadId = @ThreadId,
                    UpdatedAtUtc = SYSUTCDATETIME()
                WHERE BidId = @BidId
                  AND QuestionId = @QuestionId
                  AND UserId = @UserId;
            END
            ELSE
            BEGIN
                INSERT INTO dbo.ChatThreads (BidId, QuestionId, UserId, ThreadId, UpdatedAtUtc)
                VALUES (@BidId, @QuestionId, @UserId, @ThreadId, SYSUTCDATETIME());
            END;
            """,
            new
            {
                BidId = bidId,
                QuestionId = questionId,
                UserId = userId,
                ThreadId = threadId
            },
            cancellationToken: ct));
    }

    public async Task SetBidStatus(string bidId, BidStatus status, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bidId))
            return;

        await EnsureSchemaAsync(ct);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var payload = await connection.QuerySingleOrDefaultAsync<string>(
            new CommandDefinition(
                "SELECT Payload FROM dbo.Bids WHERE Id = @Id",
                new { Id = bidId },
                cancellationToken: ct));
        if (payload is null)
            return;

        var bid = Deserialize<BidDataModel>(payload);
        bid.Status = status;

        if (status == BidStatus.Submitted)
            await SetAllCommentsCompleteForBidAsync(connection, bid, ct);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.Bids
            SET Payload = @Payload
            WHERE Id = @Id;
            """,
            new { Id = bidId, Payload = Serialize(bid) },
            cancellationToken: ct));
    }

    public async Task<BidLibraryPushDataModel> PushBidToLibrary(
        string bidId,
        string performedByUserId,
        string performedByName,
        DateTime pushedAtUtc,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bidId))
            throw new InvalidOperationException("Invalid request, bidId is null or empty.");

        await EnsureSchemaAsync(ct);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var payload = await connection.QuerySingleOrDefaultAsync<string>(
            new CommandDefinition(
                "SELECT Payload FROM dbo.Bids WHERE Id = @Id",
                new { Id = bidId },
                cancellationToken: ct));
        if (payload is null)
            throw new InvalidOperationException("Invalid request, bid does not exist.");

        var bid = Deserialize<BidDataModel>(payload);
        if (bid.BidLibraryPush is not null)
            return bid.BidLibraryPush;

        bid.BidLibraryPush = new BidLibraryPushDataModel
        {
            BidId = bidId,
            PerformedByUserId = performedByUserId ?? string.Empty,
            PerformedByName = performedByName ?? string.Empty,
            PushedAtUtc = pushedAtUtc
        };

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.Bids
            SET Payload = @Payload
            WHERE Id = @Id;
            """,
            new { Id = bidId, Payload = Serialize(bid) },
            cancellationToken: ct));

        return bid.BidLibraryPush;
    }

    public async Task<List<QuestionAssignmentDataModel>> GetBidQuestionUsers(string bidId, string questionId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var rows = await connection.QueryAsync<(string UserId, int Role)>(new CommandDefinition(
            """
            SELECT UserId, Role
            FROM dbo.QuestionAssignments
            WHERE QuestionId = @QuestionId;
            """,
            new { QuestionId = questionId },
            cancellationToken: ct));

        return rows.Select(row => new QuestionAssignmentDataModel
        {
            UserId = row.UserId,
            Role = (QuestionUserRole)row.Role
        }).ToList();
    }

    public async Task AddBidQuestionUser(string bidId, string questionId, string userId, QuestionUserRole role,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        await EnsureSchemaAsync(ct);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        await UpsertQuestionUserAsync(connection, (SqlTransaction)tx, questionId, userId, role, ct);

        if (role == QuestionUserRole.Owner)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.QuestionAssignments
                SET Role = @ReviewerRole
                WHERE QuestionId = @QuestionId
                  AND UserId <> @UserId
                  AND Role = @OwnerRole;
                """,
                new
                {
                    ReviewerRole = (int)QuestionUserRole.Reviewer,
                    OwnerRole = (int)QuestionUserRole.Owner,
                    QuestionId = questionId,
                    UserId = userId
                },
                transaction: tx,
                cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
    }

    public async Task UpdateBidQuestionUserRole(string bidId, string questionId, string userId, QuestionUserRole role,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        await EnsureSchemaAsync(ct);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.QuestionAssignments
            SET Role = @Role
            WHERE QuestionId = @QuestionId
              AND UserId = @UserId;
            """,
            new { Role = (int)role, QuestionId = questionId, UserId = userId },
            transaction: tx,
            cancellationToken: ct));
        if (affected == 0)
        {
            await tx.CommitAsync(ct);
            return;
        }

        if (role == QuestionUserRole.Owner)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.QuestionAssignments
                SET Role = @ReviewerRole
                WHERE QuestionId = @QuestionId
                  AND UserId <> @UserId
                  AND Role = @OwnerRole;
                """,
                new
                {
                    ReviewerRole = (int)QuestionUserRole.Reviewer,
                    OwnerRole = (int)QuestionUserRole.Owner,
                    QuestionId = questionId,
                    UserId = userId
                },
                transaction: tx,
                cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
    }

    public async Task<QuestionDataModel> GetQuestion(string bidId, string questionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bidId) || string.IsNullOrWhiteSpace(questionId))
            throw new Exception("Invalid request, bidId or questionId is null or empty");

        var bid = await GetBid(bidId, ct);
        if (bid is not null)
        {
            var question = bid.Questions?.FirstOrDefault(x => x.Id == questionId);
            if (question is not null)
                return question;
        }

        throw new Exception("Invalid request, questionId does not exist");
    }

    public async Task RemoveBidQuestionUser(string bidId, string questionId, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        await EnsureSchemaAsync(ct);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM dbo.QuestionAssignments
            WHERE QuestionId = @QuestionId AND UserId = @UserId;
            """,
            new { QuestionId = questionId, UserId = userId },
            cancellationToken: ct));
    }

    public async Task<string> AddQuestionDraft(string bidId, string questionId, string response, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bidId) || string.IsNullOrWhiteSpace(questionId))
            throw new Exception("Invalid request, bidId or questionId is null or empty");

        await EnsureQuestionExistsAsync(bidId, questionId, ct);

        var draftId = Guid.NewGuid().ToString();
        var draft = new DraftDataModel(draftId)
        {
            Response = response,
            Comments = new List<DraftCommentDataModel>()
        };

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dbo.QuestionDrafts (QuestionId, DraftId, Payload)
            VALUES (@QuestionId, @DraftId, @Payload);
            """,
            new { QuestionId = questionId, DraftId = draftId, Payload = Serialize(draft) },
            cancellationToken: ct));

        return draftId;
    }

    public async Task RemoveQuestionDraft(string bidId, string questionId, string responseId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bidId) || string.IsNullOrWhiteSpace(questionId))
            return;

        await EnsureSchemaAsync(ct);
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM dbo.QuestionDrafts
            WHERE QuestionId = @QuestionId AND DraftId = @DraftId;
            """,
            new { QuestionId = questionId, DraftId = responseId },
            cancellationToken: ct));
    }

    public async Task<List<DraftDataModel>> GetQuestionDrafts(string bidId, string questionId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var payloads = await connection.QueryAsync<string>(new CommandDefinition(
            """
            SELECT Payload
            FROM dbo.QuestionDrafts
            WHERE QuestionId = @QuestionId;
            """,
            new { QuestionId = questionId },
            cancellationToken: ct));

        var drafts = payloads
            .Select(Deserialize<DraftDataModel>)
            .ToList();
        drafts.ForEach(draft => draft.Comments ??= new List<DraftCommentDataModel>());
        return drafts;
    }

    public async Task UpdateQuestionDraft(string bidId, string questionId, string draftId, string draft, CancellationToken ct)
    {
        await EnsureSchemaAsync(ct);
        var item = await GetDraftOrThrow(questionId, draftId, ct);
        item.Response = draft;
        await UpsertDraftAsync(questionId, item, ct);
    }

    public async Task<DraftCommentDataModel> AddQuestionDraftComment(
        string bidId,
        string questionId,
        string draftId,
        string comment,
        string userId,
        string authorName,
        int? startIndex,
        int? endIndex,
        string selectedText,
        CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        var draft = await GetDraftOrThrow(questionId, draftId, ct);
        draft.Comments ??= new List<DraftCommentDataModel>();

        var newComment = CreateInlineComment(comment, userId, authorName, startIndex, endIndex, selectedText);
        draft.Comments.Add(newComment);

        await UpsertDraftAsync(questionId, draft, ct);
        return newComment;
    }

    public async Task<List<DraftCommentDataModel>> GetQuestionDraftComments(string bidId, string questionId, string draftId,
        CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        var draft = await GetDraftOrThrow(questionId, draftId, ct);
        draft.Comments ??= new List<DraftCommentDataModel>();
        return draft.Comments;
    }

    public async Task<DraftCommentDataModel> SetQuestionDraftCommentCompletion(
        string bidId,
        string questionId,
        string draftId,
        string commentId,
        bool isComplete,
        CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        var draft = await GetDraftOrThrow(questionId, draftId, ct);
        draft.Comments ??= new List<DraftCommentDataModel>();
        var comment = draft.Comments.FirstOrDefault(c => string.Equals(c.Id, commentId, StringComparison.OrdinalIgnoreCase))
                      ?? throw new Exception("Invalid request, commentId does not exist");
        comment.IsComplete = isComplete;
        await UpsertDraftAsync(questionId, draft, ct);
        return comment;
    }

    public async Task CreateMentionTasks(
        string bidId,
        string questionId,
        string commentId,
        string commentText,
        List<string> mentionedUserIds,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bidId)
            || string.IsNullOrWhiteSpace(questionId)
            || string.IsNullOrWhiteSpace(commentId)
            || mentionedUserIds is null
            || mentionedUserIds.Count == 0)
            return;

        await EnsureSchemaAsync(ct);
        var validBidUsers = await GetBidUsers(bidId, ct);
        var validUsers = mentionedUserIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(id => validBidUsers.Contains(id, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (validUsers.Count == 0)
            return;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        foreach (var userId in validUsers)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM dbo.MentionTasks
                    WHERE CommentId = @CommentId
                      AND AssignedUserId = @AssignedUserId
                )
                BEGIN
                    INSERT INTO dbo.MentionTasks
                        (Id, BidId, QuestionId, CommentId, AssignedUserId, CommentText, CreatedAtUtc)
                    VALUES
                        (@Id, @BidId, @QuestionId, @CommentId, @AssignedUserId, @CommentText, SYSUTCDATETIME());
                END;
                """,
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    BidId = bidId,
                    QuestionId = questionId,
                    CommentId = commentId,
                    AssignedUserId = userId,
                    CommentText = commentText ?? string.Empty
                },
                cancellationToken: ct));
        }
    }

    public async Task<List<MentionTaskDataModel>> GetMentionTasksForUser(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return new List<MentionTaskDataModel>();

        await EnsureSchemaAsync(ct);
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        return (await connection.QueryAsync<MentionTaskDataModel>(new CommandDefinition(
            """
            SELECT Id, BidId, QuestionId, CommentId, AssignedUserId, CommentText, CreatedAtUtc
            FROM dbo.MentionTasks
            WHERE AssignedUserId = @AssignedUserId
            ORDER BY CreatedAtUtc DESC;
            """,
            new { AssignedUserId = userId },
            cancellationToken: ct))).ToList();
    }

    public async Task<List<AssignedQuestionDataModel>> GetAssignedQuestionsForUser(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return new List<AssignedQuestionDataModel>();

        await EnsureSchemaAsync(ct);
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var assignments = (await connection.QueryAsync<(string QuestionId, int Role)>(new CommandDefinition(
            """
            SELECT QuestionId, Role
            FROM dbo.QuestionAssignments
            WHERE UserId = @UserId;
            """,
            new { UserId = userId },
            cancellationToken: ct))).ToList();

        if (assignments.Count == 0)
            return new List<AssignedQuestionDataModel>();

        var assignmentByQuestionId = assignments
            .GroupBy(x => x.QuestionId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().Role, StringComparer.OrdinalIgnoreCase);

        var payloads = (await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT Payload FROM dbo.Bids",
            cancellationToken: ct))).ToList();

        var results = new List<AssignedQuestionDataModel>();
        foreach (var payload in payloads)
        {
            var bid = Deserialize<BidDataModel>(payload);
            if (bid.Status != BidStatus.Underway)
                continue;

            var bidTitle = string.IsNullOrWhiteSpace(bid.Company) ? bid.Id : bid.Company!;
            foreach (var question in bid.Questions)
            {
                if (!assignmentByQuestionId.TryGetValue(question.Id, out var role))
                    continue;

                results.Add(new AssignedQuestionDataModel
                {
                    BidId = bid.Id,
                    BidTitle = bidTitle,
                    QuestionId = question.Id,
                    QuestionTitle = string.IsNullOrWhiteSpace(question.Title)
                        ? $"#{question.Number}"
                        : question.Title,
                    Role = (QuestionUserRole)role
                });
            }
        }

        return results
            .OrderBy(x => x.BidTitle, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.QuestionTitle, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<bool> IsCommentComplete(string bidId, string questionId, string commentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(questionId) || string.IsNullOrWhiteSpace(commentId))
            return false;

        var drafts = await GetQuestionDrafts(bidId, questionId, ct);
        var draftComment = drafts
            .SelectMany(d => d.Comments ?? new List<DraftCommentDataModel>())
            .FirstOrDefault(c => string.Equals(c.Id, commentId, StringComparison.OrdinalIgnoreCase));
        if (draftComment is not null)
            return draftComment.IsComplete;

        var review = await GetRedReview(bidId, questionId, ct);
        var reviewComment = review?.Comments?
            .FirstOrDefault(c => string.Equals(c.Id, commentId, StringComparison.OrdinalIgnoreCase));
        if (reviewComment is not null)
            return reviewComment.IsComplete;

        var answer = await GetFinalAnswer(bidId, questionId, ct);
        var answerComment = answer?.Comments?
            .FirstOrDefault(c => string.Equals(c.Id, commentId, StringComparison.OrdinalIgnoreCase));
        if (answerComment is not null)
            return answerComment.IsComplete;

        return false;
    }

    public async Task<RedReviewDataModel?> GetRedReview(string bidId, string questionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(questionId))
            return null;

        await EnsureSchemaAsync(ct);
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        var payload = await connection.QuerySingleOrDefaultAsync<string>(
            new CommandDefinition(
                "SELECT Payload FROM dbo.RedReviews WHERE QuestionId = @QuestionId",
                new { QuestionId = questionId },
                cancellationToken: ct));
        if (payload is null)
            return null;

        var review = Deserialize<RedReviewDataModel>(payload);
        return new RedReviewDataModel
        {
            QuestionId = review.QuestionId,
            ResultText = review.ResultText,
            State = review.State,
            Comments = review.Comments
                .Select(c => new DraftCommentDataModel(c.Id)
                {
                    Comment = c.Comment,
                    IsComplete = c.IsComplete,
                    UserId = c.UserId,
                    AuthorName = c.AuthorName,
                    CreatedAtUtc = c.CreatedAtUtc,
                    StartIndex = c.StartIndex,
                    EndIndex = c.EndIndex,
                    SelectedText = c.SelectedText
                })
                .ToList(),
            Reviewers = review.Reviewers
                .Select(r => new RedReviewReviewerDataModel
                {
                    UserId = r.UserId,
                    State = r.State
                })
                .ToList()
        };
    }

    public async Task SetRedReview(string bidId, string questionId, RedReviewDataModel review, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(questionId))
            return;

        await EnsureSchemaAsync(ct);
        var existing = await GetRedReview(bidId, questionId, ct);
        var toStore = new RedReviewDataModel
        {
            QuestionId = questionId,
            ResultText = review.ResultText ?? string.Empty,
            State = review.State,
            Comments = (existing?.Comments ?? new List<DraftCommentDataModel>())
                .Select(c => new DraftCommentDataModel(c.Id)
                {
                    Comment = c.Comment,
                    IsComplete = c.IsComplete,
                    UserId = c.UserId,
                    AuthorName = c.AuthorName,
                    CreatedAtUtc = c.CreatedAtUtc,
                    StartIndex = c.StartIndex,
                    EndIndex = c.EndIndex,
                    SelectedText = c.SelectedText
                })
                .ToList(),
            Reviewers = review.Reviewers
                .Where(r => !string.IsNullOrWhiteSpace(r.UserId))
                .GroupBy(r => r.UserId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Select(r => new RedReviewReviewerDataModel
                {
                    UserId = r.UserId,
                    State = r.State
                })
                .ToList()
        };

        await UpsertDocumentByQuestionIdAsync("dbo.RedReviews", questionId, toStore, ct);
    }

    public async Task<FinalAnswerDataModel?> GetFinalAnswer(string bidId, string questionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(questionId))
            return null;

        await EnsureSchemaAsync(ct);
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        var payload = await connection.QuerySingleOrDefaultAsync<string>(
            new CommandDefinition(
                "SELECT Payload FROM dbo.FinalAnswers WHERE QuestionId = @QuestionId",
                new { QuestionId = questionId },
                cancellationToken: ct));
        if (payload is null)
            return null;

        var answer = Deserialize<FinalAnswerDataModel>(payload);
        return new FinalAnswerDataModel
        {
            QuestionId = answer.QuestionId,
            AnswerText = answer.AnswerText,
            ReadyForSubmission = answer.ReadyForSubmission,
            Comments = answer.Comments
                .Select(c => new DraftCommentDataModel(c.Id)
                {
                    Comment = c.Comment,
                    IsComplete = c.IsComplete,
                    UserId = c.UserId,
                    AuthorName = c.AuthorName,
                    CreatedAtUtc = c.CreatedAtUtc,
                    StartIndex = c.StartIndex,
                    EndIndex = c.EndIndex,
                    SelectedText = c.SelectedText
                })
                .ToList()
        };
    }

    public async Task SetFinalAnswer(string bidId, string questionId, FinalAnswerDataModel answer, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(questionId))
            return;

        await EnsureSchemaAsync(ct);
        var existing = await GetFinalAnswer(bidId, questionId, ct);
        var toStore = new FinalAnswerDataModel
        {
            QuestionId = questionId,
            AnswerText = answer.AnswerText ?? string.Empty,
            ReadyForSubmission = answer.ReadyForSubmission,
            Comments = (existing?.Comments ?? new List<DraftCommentDataModel>())
                .Select(c => new DraftCommentDataModel(c.Id)
                {
                    Comment = c.Comment,
                    IsComplete = c.IsComplete,
                    UserId = c.UserId,
                    AuthorName = c.AuthorName,
                    CreatedAtUtc = c.CreatedAtUtc,
                    StartIndex = c.StartIndex,
                    EndIndex = c.EndIndex,
                    SelectedText = c.SelectedText
                })
                .ToList()
        };

        await UpsertDocumentByQuestionIdAsync("dbo.FinalAnswers", questionId, toStore, ct);
    }

    public async Task<DraftCommentDataModel> AddRedReviewComment(
        string bidId,
        string questionId,
        string comment,
        string userId,
        string authorName,
        int? startIndex,
        int? endIndex,
        string selectedText,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(questionId))
            throw new Exception("Invalid request, questionId is null or empty");

        var review = await GetRedReview(bidId, questionId, ct) ?? new RedReviewDataModel { QuestionId = questionId };
        review.Comments ??= new List<DraftCommentDataModel>();

        var newComment = CreateInlineComment(comment, userId, authorName, startIndex, endIndex, selectedText);
        review.Comments.Add(newComment);

        await UpsertDocumentByQuestionIdAsync("dbo.RedReviews", questionId, review, ct);
        return newComment;
    }

    public async Task<DraftCommentDataModel> AddFinalAnswerComment(
        string bidId,
        string questionId,
        string comment,
        string userId,
        string authorName,
        int? startIndex,
        int? endIndex,
        string selectedText,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(questionId))
            throw new Exception("Invalid request, questionId is null or empty");

        var answer = await GetFinalAnswer(bidId, questionId, ct) ?? new FinalAnswerDataModel { QuestionId = questionId };
        answer.Comments ??= new List<DraftCommentDataModel>();

        var newComment = CreateInlineComment(comment, userId, authorName, startIndex, endIndex, selectedText);
        answer.Comments.Add(newComment);

        await UpsertDocumentByQuestionIdAsync("dbo.FinalAnswers", questionId, answer, ct);
        return newComment;
    }

    public async Task<DraftCommentDataModel> SetRedReviewCommentCompletion(
        string bidId,
        string questionId,
        string commentId,
        bool isComplete,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(questionId))
            throw new Exception("Invalid request, questionId is null or empty");

        var review = await GetRedReview(bidId, questionId, ct) ?? throw new Exception("Invalid request, red review does not exist");
        review.Comments ??= new List<DraftCommentDataModel>();
        var comment = review.Comments.FirstOrDefault(c => string.Equals(c.Id, commentId, StringComparison.OrdinalIgnoreCase))
                      ?? throw new Exception("Invalid request, commentId does not exist");
        comment.IsComplete = isComplete;
        await UpsertDocumentByQuestionIdAsync("dbo.RedReviews", questionId, review, ct);
        return comment;
    }

    public async Task<DraftCommentDataModel> SetFinalAnswerCommentCompletion(
        string bidId,
        string questionId,
        string commentId,
        bool isComplete,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(questionId))
            throw new Exception("Invalid request, questionId is null or empty");

        var answer = await GetFinalAnswer(bidId, questionId, ct) ?? throw new Exception("Invalid request, final answer does not exist");
        answer.Comments ??= new List<DraftCommentDataModel>();
        var comment = answer.Comments.FirstOrDefault(c => string.Equals(c.Id, commentId, StringComparison.OrdinalIgnoreCase))
                      ?? throw new Exception("Invalid request, commentId does not exist");
        comment.IsComplete = isComplete;
        await UpsertDocumentByQuestionIdAsync("dbo.FinalAnswers", questionId, answer, ct);
        return comment;
    }

    private async Task EnsureQuestionExistsAsync(string bidId, string questionId, CancellationToken ct)
    {
        var question = await GetQuestion(bidId, questionId, ct);
        if (question is null)
            throw new Exception("Invalid request, questionId does not exist");
    }

    private async Task<DraftDataModel> GetDraftOrThrow(string questionId, string draftId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(questionId) || string.IsNullOrWhiteSpace(draftId))
            throw new Exception("Invalid request, questionId or draftId is null or empty");

        await EnsureSchemaAsync(ct);
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        var payload = await connection.QuerySingleOrDefaultAsync<string>(
            new CommandDefinition(
                """
                SELECT Payload
                FROM dbo.QuestionDrafts
                WHERE QuestionId = @QuestionId
                  AND DraftId = @DraftId;
                """,
                new { QuestionId = questionId, DraftId = draftId },
                cancellationToken: ct));
        if (payload is null)
            throw new Exception("Invalid request, draftId does not exist");

        return Deserialize<DraftDataModel>(payload);
    }

    private async Task UpsertDraftAsync(string questionId, DraftDataModel draft, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            IF EXISTS (
                SELECT 1 FROM dbo.QuestionDrafts WHERE QuestionId = @QuestionId AND DraftId = @DraftId
            )
            BEGIN
                UPDATE dbo.QuestionDrafts
                SET Payload = @Payload
                WHERE QuestionId = @QuestionId AND DraftId = @DraftId;
            END
            ELSE
            BEGIN
                INSERT INTO dbo.QuestionDrafts (QuestionId, DraftId, Payload)
                VALUES (@QuestionId, @DraftId, @Payload);
            END;
            """,
            new { QuestionId = questionId, DraftId = draft.Id, Payload = Serialize(draft) },
            cancellationToken: ct));
    }

    private async Task UpsertDocumentByQuestionIdAsync<T>(string tableName, string questionId, T model, CancellationToken ct)
    {
        await EnsureSchemaAsync(ct);
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            $"""
            IF EXISTS (SELECT 1 FROM {tableName} WHERE QuestionId = @QuestionId)
            BEGIN
                UPDATE {tableName}
                SET Payload = @Payload
                WHERE QuestionId = @QuestionId;
            END
            ELSE
            BEGIN
                INSERT INTO {tableName} (QuestionId, Payload)
                VALUES (@QuestionId, @Payload);
            END;
            """,
            new { QuestionId = questionId, Payload = Serialize(model) },
            cancellationToken: ct));
    }

    private async Task SetAllCommentsCompleteForBidAsync(
        SqlConnection connection,
        BidDataModel bid,
        CancellationToken ct)
    {
        if (bid.Questions is null || bid.Questions.Count == 0)
            return;

        foreach (var question in bid.Questions)
        {
            if (string.IsNullOrWhiteSpace(question.Id))
                continue;

            var questionId = question.Id;

            var draftRows = (await connection.QueryAsync<(string DraftId, string Payload)>(new CommandDefinition(
                """
                SELECT DraftId, Payload
                FROM dbo.QuestionDrafts
                WHERE QuestionId = @QuestionId;
                """,
                new { QuestionId = questionId },
                cancellationToken: ct))).ToList();

            foreach (var row in draftRows)
            {
                var draft = Deserialize<DraftDataModel>(row.Payload);
                SetCommentsComplete(draft.Comments);

                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE dbo.QuestionDrafts
                    SET Payload = @Payload
                    WHERE QuestionId = @QuestionId AND DraftId = @DraftId;
                    """,
                    new
                    {
                        QuestionId = questionId,
                        DraftId = row.DraftId,
                        Payload = Serialize(draft)
                    },
                    cancellationToken: ct));
            }

            var redReviewPayload = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                """
                SELECT Payload
                FROM dbo.RedReviews
                WHERE QuestionId = @QuestionId;
                """,
                new { QuestionId = questionId },
                cancellationToken: ct));
            if (redReviewPayload is not null)
            {
                var review = Deserialize<RedReviewDataModel>(redReviewPayload);
                SetCommentsComplete(review.Comments);

                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE dbo.RedReviews
                    SET Payload = @Payload
                    WHERE QuestionId = @QuestionId;
                    """,
                    new { QuestionId = questionId, Payload = Serialize(review) },
                    cancellationToken: ct));
            }

            var finalAnswerPayload = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                """
                SELECT Payload
                FROM dbo.FinalAnswers
                WHERE QuestionId = @QuestionId;
                """,
                new { QuestionId = questionId },
                cancellationToken: ct));
            if (finalAnswerPayload is not null)
            {
                var answer = Deserialize<FinalAnswerDataModel>(finalAnswerPayload);
                SetCommentsComplete(answer.Comments);

                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE dbo.FinalAnswers
                    SET Payload = @Payload
                    WHERE QuestionId = @QuestionId;
                    """,
                    new { QuestionId = questionId, Payload = Serialize(answer) },
                    cancellationToken: ct));
            }
        }
    }

    private static void SetCommentsComplete(List<DraftCommentDataModel>? comments)
    {
        if (comments is null || comments.Count == 0)
            return;

        foreach (var comment in comments)
            comment.IsComplete = true;
    }

    private async Task UpsertQuestionUserAsync(
        SqlConnection connection,
        SqlTransaction tx,
        string questionId,
        string userId,
        QuestionUserRole role,
        CancellationToken ct)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            """
            IF EXISTS (
                SELECT 1 FROM dbo.QuestionAssignments WHERE QuestionId = @QuestionId AND UserId = @UserId
            )
            BEGIN
                UPDATE dbo.QuestionAssignments
                SET Role = @Role
                WHERE QuestionId = @QuestionId AND UserId = @UserId;
            END
            ELSE
            BEGIN
                INSERT INTO dbo.QuestionAssignments (QuestionId, UserId, Role)
                VALUES (@QuestionId, @UserId, @Role);
            END;
            """,
            new { QuestionId = questionId, UserId = userId, Role = (int)role },
            transaction: tx,
            cancellationToken: ct));
    }

    private async Task EnsureSchemaAsync(CancellationToken ct)
    {
        if (_schemaInitialized)
            return;

        await SchemaLock.WaitAsync(ct);
        try
        {
            if (_schemaInitialized)
                return;

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);
            await connection.ExecuteAsync(new CommandDefinition(
                """
                IF OBJECT_ID(N'dbo.Bids', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.Bids
                    (
                        Id NVARCHAR(100) NOT NULL PRIMARY KEY,
                        Payload NVARCHAR(MAX) NOT NULL,
                        CreatedAtUtc DATETIMEOFFSET(7) NOT NULL
                            CONSTRAINT DF_Bids_CreatedAtUtc DEFAULT SYSUTCDATETIME()
                    );
                END;

                IF OBJECT_ID(N'dbo.BidUsers', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.BidUsers
                    (
                        BidId NVARCHAR(100) NOT NULL,
                        UserId NVARCHAR(100) NOT NULL,
                        CONSTRAINT PK_BidUsers PRIMARY KEY (BidId, UserId)
                    );
                END;

                IF OBJECT_ID(N'dbo.BidFiles', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.BidFiles
                    (
                        Id NVARCHAR(100) NOT NULL PRIMARY KEY,
                        BidId NVARCHAR(100) NOT NULL,
                        FileName NVARCHAR(400) NOT NULL,
                        ContentType NVARCHAR(200) NOT NULL,
                        SizeBytes BIGINT NOT NULL,
                        Content VARBINARY(MAX) NOT NULL,
                        UploadedAtUtc DATETIMEOFFSET(7) NOT NULL
                    );

                    CREATE INDEX IX_BidFiles_BidId_UploadedAtUtc
                        ON dbo.BidFiles (BidId, UploadedAtUtc DESC);
                END;

                IF OBJECT_ID(N'dbo.QuestionAssignments', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.QuestionAssignments
                    (
                        QuestionId NVARCHAR(100) NOT NULL,
                        UserId NVARCHAR(100) NOT NULL,
                        Role INT NOT NULL,
                        CONSTRAINT PK_QuestionAssignments PRIMARY KEY (QuestionId, UserId)
                    );
                END;

                IF OBJECT_ID(N'dbo.QuestionDrafts', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.QuestionDrafts
                    (
                        QuestionId NVARCHAR(100) NOT NULL,
                        DraftId NVARCHAR(100) NOT NULL,
                        Payload NVARCHAR(MAX) NOT NULL,
                        CONSTRAINT PK_QuestionDrafts PRIMARY KEY (QuestionId, DraftId)
                    );
                END;

                IF OBJECT_ID(N'dbo.RedReviews', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.RedReviews
                    (
                        QuestionId NVARCHAR(100) NOT NULL PRIMARY KEY,
                        Payload NVARCHAR(MAX) NOT NULL
                    );
                END;

                IF OBJECT_ID(N'dbo.FinalAnswers', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.FinalAnswers
                    (
                        QuestionId NVARCHAR(100) NOT NULL PRIMARY KEY,
                        Payload NVARCHAR(MAX) NOT NULL
                    );
                END;

                IF OBJECT_ID(N'dbo.MentionTasks', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.MentionTasks
                    (
                        Id NVARCHAR(100) NOT NULL PRIMARY KEY,
                        BidId NVARCHAR(100) NOT NULL,
                        QuestionId NVARCHAR(100) NOT NULL,
                        CommentId NVARCHAR(100) NOT NULL,
                        AssignedUserId NVARCHAR(100) NOT NULL,
                        CommentText NVARCHAR(MAX) NOT NULL,
                        CreatedAtUtc DATETIMEOFFSET(7) NOT NULL
                    );

                    CREATE UNIQUE INDEX UX_MentionTasks_CommentUser
                        ON dbo.MentionTasks (CommentId, AssignedUserId);

                    CREATE INDEX IX_MentionTasks_AssignedUser
                        ON dbo.MentionTasks (AssignedUserId, CreatedAtUtc DESC);
                END;

                IF OBJECT_ID(N'dbo.ChatThreads', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.ChatThreads
                    (
                        BidId NVARCHAR(100) NOT NULL,
                        QuestionId NVARCHAR(100) NOT NULL,
                        UserId NVARCHAR(200) NOT NULL,
                        ThreadId NVARCHAR(200) NOT NULL,
                        UpdatedAtUtc DATETIMEOFFSET(7) NOT NULL,
                        CONSTRAINT PK_ChatThreads PRIMARY KEY (BidId, QuestionId, UserId)
                    );
                END;

                IF OBJECT_ID(N'dbo.DocumentIngestionJobs', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.DocumentIngestionJobs
                    (
                        JobId NVARCHAR(100) NOT NULL PRIMARY KEY,
                        OwnerUserKey NVARCHAR(200) NOT NULL,
                        FileName NVARCHAR(400) NOT NULL,
                        Stage NVARCHAR(50) NOT NULL,
                        Status NVARCHAR(100) NOT NULL,
                        Message NVARCHAR(MAX) NOT NULL,
                        IsComplete BIT NOT NULL,
                        IsError BIT NOT NULL,
                        CreatedAtUtc DATETIMEOFFSET(7) NOT NULL,
                        UpdatedAtUtc DATETIMEOFFSET(7) NOT NULL,
                        CompletedAtUtc DATETIMEOFFSET(7) NULL,
                        ResultPayload NVARCHAR(MAX) NULL
                    );

                    CREATE INDEX IX_DocumentIngestionJobs_OwnerUserKey_CreatedAtUtc
                        ON dbo.DocumentIngestionJobs (OwnerUserKey, CreatedAtUtc DESC);
                END;
                """,
                cancellationToken: ct));
            _schemaInitialized = true;
        }
        finally
        {
            SchemaLock.Release();
        }
    }

    private static DraftCommentDataModel CreateInlineComment(
        string comment,
        string userId,
        string authorName,
        int? startIndex,
        int? endIndex,
        string selectedText)
    {
        return new DraftCommentDataModel(Guid.NewGuid().ToString())
        {
            Comment = comment ?? string.Empty,
            IsComplete = false,
            UserId = userId ?? string.Empty,
            AuthorName = authorName ?? string.Empty,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            StartIndex = startIndex,
            EndIndex = endIndex,
            SelectedText = selectedText ?? string.Empty
        };
    }

    private static T Deserialize<T>(string payload)
    {
        return JsonSerializer.Deserialize<T>(payload, JsonOptions)
               ?? throw new InvalidOperationException("Failed to deserialize SQL payload.");
    }

    private static DocumentIngestionJobDataModel MapDocumentIngestionJob(DocumentIngestionJobRow row)
    {
        return new DocumentIngestionJobDataModel
        {
            JobId = row.JobId,
            OwnerUserKey = row.OwnerUserKey,
            FileName = row.FileName,
            Stage = Enum.TryParse<BidStage>(row.Stage, true, out var stage) ? stage : BidStage.Stage1,
            Status = row.Status,
            Message = row.Message,
            IsComplete = row.IsComplete,
            IsError = row.IsError,
            CreatedAtUtc = row.CreatedAtUtc,
            UpdatedAtUtc = row.UpdatedAtUtc,
            CompletedAtUtc = row.CompletedAtUtc,
            Result = string.IsNullOrWhiteSpace(row.ResultPayload)
                ? null
                : Deserialize<ParsedDocumentResponse>(row.ResultPayload)
        };
    }

    private static string Serialize<T>(T model) => JsonSerializer.Serialize(model, JsonOptions);

    private sealed class DocumentIngestionJobRow
    {
        public string JobId { get; set; } = string.Empty;
        public string OwnerUserKey { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Stage { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public bool IsComplete { get; set; }
        public bool IsError { get; set; }
        public DateTimeOffset CreatedAtUtc { get; set; }
        public DateTimeOffset UpdatedAtUtc { get; set; }
        public DateTimeOffset? CompletedAtUtc { get; set; }
        public string? ResultPayload { get; set; }
    }
}
