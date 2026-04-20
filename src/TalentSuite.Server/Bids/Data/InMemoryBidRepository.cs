using TalentSuite.Server.Bids.Data.Models;
using TalentSuite.Shared.Bids;

namespace TalentSuite.Server.Bids.Data;

public class InMemoryBidRepository : IManageBids
{
    private readonly Dictionary<string, BidDataModel> _bids = new();
    private readonly Dictionary<string, List<string>> _usersForBids = new();
    private readonly Dictionary<string, List<BidFileDataModel>> _filesForBids = new();
    private readonly Dictionary<string, DocumentIngestionJobDataModel> _documentIngestionJobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _chatThreadIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<QuestionAssignmentDataModel>> _usersForQuestions = new();
    private readonly Dictionary<string, List<DraftDataModel>> _draftsForQuestions = new();
    private readonly Dictionary<string, RedReviewDataModel> _redReviewsForQuestions = new();
    private readonly Dictionary<string, FinalAnswerDataModel> _finalAnswersForQuestions = new();
    private readonly List<MentionTaskDataModel> _mentionTasks = new();
    
    public InMemoryBidRepository()
    {
        // var id = "04d3fde7-8b47-4558-905b-1888fb8a4db0";
        //
        // _bids.Add(id, new BidDataModel(id)
        // {
        //     Budget = "£1000000",
        //     Company = "TouchScreenData",
        //     DeadlineForQualifying = "March 13",
        //     DeadlineForSubmission = "April 13",
        //     LengthOfContract = "2 years plus",
        //     Summary = "Summary",
        //     Status = BidStatus.Underway,
        //     Questions =
        //     [
        //         new QuestionDataModel("44d3fde7-8b47-4558-905b-1888fb8a4db3")
        //         {
        //             Category = "Nice to have",
        //             Description = "desc",
        //             NiceToHave = true,
        //             Required = false,
        //             Number = "1",
        //             Title = "This is a question",
        //             Weighting = 100
        //         }
        //     ]
        // });
        //
        // _usersForBids[id] = new List<string>();
        // _usersForQuestions.Add("44d3fde7-8b47-4558-905b-1888fb8a4db3", new List<QuestionAssignmentDataModel>());
        // _draftsForQuestions.Add("44d3fde7-8b47-4558-905b-1888fb8a4db3", new List<DraftDataModel>());
        // _redReviewsForQuestions.Add("44d3fde7-8b47-4558-905b-1888fb8a4db3", new RedReviewDataModel
        // {
        //     QuestionId = "44d3fde7-8b47-4558-905b-1888fb8a4db3"
        // });
        // _finalAnswersForQuestions.Add("44d3fde7-8b47-4558-905b-1888fb8a4db3", new FinalAnswerDataModel
        // {
        //     QuestionId = "44d3fde7-8b47-4558-905b-1888fb8a4db3"
        // });
    }

    public async Task<Guid> StoreBid(BidDataModel request, CancellationToken ct = default)
    {
        var guid = Guid.NewGuid();
        request.Id = guid.ToString();
        
        request.Questions.ForEach(q =>
        {
            q.Id = Guid.NewGuid().ToString();
            _usersForQuestions[q.Id] = new List<QuestionAssignmentDataModel>();
            _draftsForQuestions[q.Id] = new List<DraftDataModel>();
            _redReviewsForQuestions[q.Id] = new RedReviewDataModel
            {
                QuestionId = q.Id,
                Comments = new List<DraftCommentDataModel>()
            };
            _finalAnswersForQuestions[q.Id] = new FinalAnswerDataModel
            {
                QuestionId = q.Id,
                Comments = new List<DraftCommentDataModel>()
            };
        });

        _bids.Add(request.Id, request);
        _usersForBids.TryAdd(request.Id, new List<string>());
        _filesForBids.TryAdd(request.Id, new List<BidFileDataModel>());
        
        return await Task.FromResult(guid);
    }

    public async Task<BidDataModel> GetBid(string id, CancellationToken ct = default)
    {
        return await Task.FromResult(_bids.FirstOrDefault(x => x.Key == id).Value);
    }

    public async Task<SearchDataModel> SearchBids(int page, int pageSize, CancellationToken ct = default)
    {
        var bids = _bids.Values
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        // Build the lightweight search projection manually so we always emit
        // an accurate question count (the previous projection
        // was returning 0 because the questions collection was not being
        // populated during the map).
        var items = bids
            .Select(b => new SearchItemDataModel
            {
                Id = b.Id,
                Company = b.Company ?? string.Empty,
                Summary = b.Summary ?? string.Empty,
                QuestionCount = b.Questions?.Count ?? 0,
                Status = b.Status
            })
            .ToList();

        var model = new SearchDataModel
        {
            CurrentPage = page,
            PageSize = pageSize,
            Items = items,
            TotalCount = _bids.Count
        };
        
        return await Task.FromResult(model);
    }

    public Task SaveDocumentIngestionJob(DocumentIngestionJobDataModel job, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        _documentIngestionJobs[job.JobId] = CloneDocumentIngestionJob(job);
        return Task.CompletedTask;
    }

    public Task<DocumentIngestionJobDataModel?> GetDocumentIngestionJob(string jobId, CancellationToken ct = default)
    {
        if (_documentIngestionJobs.TryGetValue(jobId, out var job))
            return Task.FromResult<DocumentIngestionJobDataModel?>(CloneDocumentIngestionJob(job));

        return Task.FromResult<DocumentIngestionJobDataModel?>(null);
    }

    public Task<List<DocumentIngestionJobDataModel>> GetDocumentIngestionJobsForUser(string ownerUserKey, CancellationToken ct = default)
    {
        var jobs = _documentIngestionJobs.Values
            .Where(job => string.Equals(job.OwnerUserKey, ownerUserKey, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(job => job.CreatedAtUtc)
            .Select(CloneDocumentIngestionJob)
            .ToList();

        return Task.FromResult(jobs);
    }

    public async Task<List<string>> GetBidUsers(string bidId, CancellationToken ct = default)
    {
        if (_usersForBids.TryGetValue(bidId, out var users))
            return await Task.FromResult(users);

        return await Task.FromResult(new List<string>());
    }

    public async Task AddBidUser(string bidId, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        if (!_usersForBids.TryGetValue(bidId, out var users))
        {
            users = new List<string>();
            _usersForBids[bidId] = users;
        }

        if (!users.Contains(userId))
            users.Add(userId);

        await Task.FromResult(0);
    }

    public async Task RemoveBidUser(string bidId, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        if (_usersForBids.TryGetValue(bidId, out var users))
            users.Remove(userId);

        await Task.FromResult(0);
    }

    public Task<List<BidFileDataModel>> GetBidFiles(string bidId, CancellationToken ct = default)
    {
        if (_filesForBids.TryGetValue(bidId, out var files))
        {
            return Task.FromResult(files
                .OrderByDescending(x => x.UploadedAtUtc)
                .Select(x => new BidFileDataModel
                {
                    Id = x.Id,
                    BidId = x.BidId,
                    FileName = x.FileName,
                    ContentType = x.ContentType,
                    SizeBytes = x.SizeBytes,
                    UploadedAtUtc = x.UploadedAtUtc,
                    Content = x.Content.ToArray()
                })
                .ToList());
        }

        return Task.FromResult(new List<BidFileDataModel>());
    }

    public Task<BidFileDataModel> AddBidFile(
        string bidId,
        string fileName,
        string contentType,
        byte[] content,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bidId))
            throw new InvalidOperationException("Invalid request, bidId is null or empty.");

        if (!_bids.ContainsKey(bidId))
            throw new InvalidOperationException("Invalid request, bid does not exist.");

        if (!_filesForBids.TryGetValue(bidId, out var files))
        {
            files = new List<BidFileDataModel>();
            _filesForBids[bidId] = files;
        }

        var file = new BidFileDataModel
        {
            Id = Guid.NewGuid().ToString(),
            BidId = bidId,
            FileName = fileName ?? string.Empty,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            SizeBytes = content?.LongLength ?? 0L,
            UploadedAtUtc = DateTime.UtcNow,
            Content = content?.ToArray() ?? Array.Empty<byte>()
        };

        files.Add(file);
        return Task.FromResult(file);
    }

    public Task<BidFileDataModel?> GetBidFile(string bidId, string fileId, CancellationToken ct = default)
    {
        if (!_filesForBids.TryGetValue(bidId, out var files))
            return Task.FromResult<BidFileDataModel?>(null);

        var file = files.FirstOrDefault(x => string.Equals(x.Id, fileId, StringComparison.OrdinalIgnoreCase));
        if (file is null)
            return Task.FromResult<BidFileDataModel?>(null);

        return Task.FromResult<BidFileDataModel?>(new BidFileDataModel
        {
            Id = file.Id,
            BidId = file.BidId,
            FileName = file.FileName,
            ContentType = file.ContentType,
            SizeBytes = file.SizeBytes,
            UploadedAtUtc = file.UploadedAtUtc,
            Content = file.Content.ToArray()
        });
    }

    public Task<bool> DeleteBidFile(string bidId, string fileId, CancellationToken ct = default)
    {
        if (!_filesForBids.TryGetValue(bidId, out var files))
            return Task.FromResult(false);

        var removed = files.RemoveAll(x => string.Equals(x.Id, fileId, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(removed > 0);
    }

    public Task<string?> GetChatThreadId(string bidId, string questionId, string userId, CancellationToken ct = default)
    {
        var key = BuildChatThreadKey(bidId, questionId, userId);
        if (_chatThreadIds.TryGetValue(key, out var threadId))
            return Task.FromResult<string?>(threadId);

        return Task.FromResult<string?>(null);
    }

    public Task SetChatThreadId(string bidId, string questionId, string userId, string threadId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(threadId))
            return Task.CompletedTask;

        var key = BuildChatThreadKey(bidId, questionId, userId);
        _chatThreadIds[key] = threadId;
        return Task.CompletedTask;
    }

    public Task SetBidStatus(string bidId, BidStatus status, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bidId))
            return Task.CompletedTask;

        if (_bids.TryGetValue(bidId, out var bid))
        {
            bid.Status = status;
            if (status == BidStatus.Submitted)
                SetAllCommentsCompleteForBid(bid);
        }

        return Task.CompletedTask;
    }

    public Task<BidLibraryPushDataModel> PushBidToLibrary(
        string bidId,
        string performedByUserId,
        string performedByName,
        DateTime pushedAtUtc,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bidId))
            throw new InvalidOperationException("Invalid request, bidId is null or empty.");

        if (!_bids.TryGetValue(bidId, out var bid))
            throw new InvalidOperationException("Invalid request, bid does not exist.");

        if (bid.BidLibraryPush is not null)
            return Task.FromResult(bid.BidLibraryPush);

        bid.BidLibraryPush = new BidLibraryPushDataModel
        {
            BidId = bidId,
            PerformedByUserId = performedByUserId ?? string.Empty,
            PerformedByName = performedByName ?? string.Empty,
            PushedAtUtc = pushedAtUtc
        };

        return Task.FromResult(bid.BidLibraryPush);
    }

    public async Task<List<QuestionAssignmentDataModel>> GetBidQuestionUsers(string bidId, string questionId, CancellationToken ct = default)
    {
        if (_usersForQuestions.TryGetValue(questionId, out var users)) 
            return await Task.FromResult(users.ToList());

        return await Task.FromResult(new List<QuestionAssignmentDataModel>());
    }

    public async Task AddBidQuestionUser(string bidId, string questionId, string userId, QuestionUserRole role, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        if (!_usersForQuestions.TryGetValue(questionId, out var users))
        {
            users = new List<QuestionAssignmentDataModel>();
            _usersForQuestions[questionId] = users;
        }

        var existing = users.FirstOrDefault(x => x.UserId == userId);
        if (existing is not null)
        {
            existing.Role = role;
        }
        else
        {
            users.Add(new QuestionAssignmentDataModel
            {
                UserId = userId,
                Role = role
            });
        }

        if (role == QuestionUserRole.Owner)
        {
            foreach (var assignment in users.Where(x => x.UserId != userId && x.Role == QuestionUserRole.Owner))
                assignment.Role = QuestionUserRole.Reviewer;
        }
        
        await Task.FromResult(0);
    }

    public async Task<QuestionDataModel> GetQuestion(string bidId, string questionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bidId) || string.IsNullOrWhiteSpace(questionId))
            throw new  Exception("Invalid request, bidId or questionId is null or empty");
        
        if (_bids.TryGetValue(bidId, out var bid))
        {
            var question = bid.Questions?.FirstOrDefault(x => x.Id == questionId);

            if (question is not null)
            {
                return await Task.FromResult(question);
            }
        }
        
        throw new Exception("Invalid request, questionId does not exist");
    }

    public async Task RemoveBidQuestionUser(string bidId, string questionId, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        if (_usersForQuestions.TryGetValue(questionId, out var users))
            users.RemoveAll(x => x.UserId == userId);
        
        await Task.FromResult(0);
    }

    public async Task UpdateBidQuestionUserRole(string bidId, string questionId, string userId, QuestionUserRole role, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        if (!_usersForQuestions.TryGetValue(questionId, out var users))
            return;

        var existing = users.FirstOrDefault(x => x.UserId == userId);
        if (existing is null)
            return;

        existing.Role = role;

        if (role == QuestionUserRole.Owner)
        {
            foreach (var assignment in users.Where(x => x.UserId != userId && x.Role == QuestionUserRole.Owner))
                assignment.Role = QuestionUserRole.Reviewer;
        }

        await Task.FromResult(0);
    }

    public async Task<string> AddQuestionDraft(string bidId, string questionId, string response, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bidId) || string.IsNullOrWhiteSpace(questionId))
            throw new Exception("Invalid request, bidId or questionId is null or empty");

        if (_draftsForQuestions.TryGetValue(questionId, out var responses))
        {
            var newId = Guid.NewGuid().ToString();
            
            responses.Add(new DraftDataModel(newId)
            {
                Response = response,
                Comments = new List<DraftCommentDataModel>()
            });

            return await Task.FromResult(newId);
        }
            
        throw new Exception("Invalid request, questionId does not exist");
    }

    public async Task RemoveQuestionDraft(string bidId, string questionId, string responseId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bidId) || string.IsNullOrWhiteSpace(questionId))
            return;

        if (_draftsForQuestions.TryGetValue(questionId, out var responses))
        {
            responses.RemoveAll(r => r.Id == responseId);
        }
            
        await Task.FromResult(0);
    }

    public async Task<List<DraftDataModel>> GetQuestionDrafts(string bidId, string questionId, CancellationToken ct = default)
    {
        if (_draftsForQuestions.TryGetValue(questionId, out var responses))
        {
            foreach (var response in responses)
                response.Comments ??= new List<DraftCommentDataModel>();

            return responses;
        } 
        
        return await Task.FromResult(new List<DraftDataModel>());
    }

    public Task UpdateQuestionDraft(string bidId, string questionId, string draftId, string draft, CancellationToken ct)
    {
        if (_draftsForQuestions.TryGetValue(questionId, out var responses))
        {
            responses.Where(s => s.Id == draftId)
                .ToList().ForEach(s => s.Response = draft);
        }
        
        return Task.FromResult(0);
    }

    public Task<DraftCommentDataModel> AddQuestionDraftComment(
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
        var draft = GetDraftOrThrow(questionId, draftId);

        draft.Comments ??= new List<DraftCommentDataModel>();

        var newComment = new DraftCommentDataModel(Guid.NewGuid().ToString())
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

        draft.Comments.Add(newComment);
        return Task.FromResult(newComment);
    }

    public Task<List<DraftCommentDataModel>> GetQuestionDraftComments(string bidId, string questionId, string draftId, CancellationToken ct = default)
    {
        var draft = GetDraftOrThrow(questionId, draftId);
        draft.Comments ??= new List<DraftCommentDataModel>();

        return Task.FromResult(draft.Comments);
    }

    public Task<DraftCommentDataModel> SetQuestionDraftCommentCompletion(
        string bidId,
        string questionId,
        string draftId,
        string commentId,
        bool isComplete,
        CancellationToken ct = default)
    {
        var draft = GetDraftOrThrow(questionId, draftId);
        draft.Comments ??= new List<DraftCommentDataModel>();
        var comment = draft.Comments.FirstOrDefault(c => string.Equals(c.Id, commentId, StringComparison.OrdinalIgnoreCase))
                      ?? throw new Exception("Invalid request, commentId does not exist");
        comment.IsComplete = isComplete;
        return Task.FromResult(comment);
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

        var validBidUsers = await GetBidUsers(bidId, ct);
        var validUsers = mentionedUserIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(id => validBidUsers.Contains(id, StringComparer.OrdinalIgnoreCase))
            .ToList();

        foreach (var userId in validUsers)
        {
            var alreadyExists = _mentionTasks.Any(t =>
                string.Equals(t.CommentId, commentId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(t.AssignedUserId, userId, StringComparison.OrdinalIgnoreCase));
            if (alreadyExists)
                continue;

            _mentionTasks.Add(new MentionTaskDataModel
            {
                Id = Guid.NewGuid().ToString(),
                BidId = bidId,
                QuestionId = questionId,
                CommentId = commentId,
                AssignedUserId = userId,
                CommentText = commentText ?? string.Empty,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
        }
    }

    public Task<List<MentionTaskDataModel>> GetMentionTasksForUser(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return Task.FromResult(new List<MentionTaskDataModel>());

        var tasks = _mentionTasks
            .Where(t => string.Equals(t.AssignedUserId, userId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(t => t.CreatedAtUtc)
            .Select(t => new MentionTaskDataModel
            {
                Id = t.Id,
                BidId = t.BidId,
                QuestionId = t.QuestionId,
                CommentId = t.CommentId,
                AssignedUserId = t.AssignedUserId,
                CommentText = t.CommentText,
                CreatedAtUtc = t.CreatedAtUtc
            })
            .ToList();

        return Task.FromResult(tasks);
    }

    public Task<List<AssignedQuestionDataModel>> GetAssignedQuestionsForUser(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return Task.FromResult(new List<AssignedQuestionDataModel>());

        var assigned = new List<AssignedQuestionDataModel>();

        foreach (var bid in _bids.Values)
        {
            if (bid.Status != BidStatus.Underway)
                continue;

            var bidTitle = string.IsNullOrWhiteSpace(bid.Company) ? bid.Id : bid.Company!;
            foreach (var question in bid.Questions)
            {
                if (!_usersForQuestions.TryGetValue(question.Id, out var assignments))
                    continue;

                var match = assignments.FirstOrDefault(a =>
                    string.Equals(a.UserId, userId, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                    continue;

                assigned.Add(new AssignedQuestionDataModel
                {
                    BidId = bid.Id,
                    BidTitle = bidTitle,
                    QuestionId = question.Id,
                    QuestionTitle = string.IsNullOrWhiteSpace(question.Title)
                        ? $"#{question.Number}"
                        : question.Title,
                    Role = match.Role
                });
            }
        }

        return Task.FromResult(
            assigned
                .OrderBy(x => x.BidTitle, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.QuestionTitle, StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    public Task<bool> IsCommentComplete(string bidId, string questionId, string commentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(questionId) || string.IsNullOrWhiteSpace(commentId))
            return Task.FromResult(false);

        if (_draftsForQuestions.TryGetValue(questionId, out var drafts))
        {
            var draftComment = drafts
                .SelectMany(d => d.Comments ?? new List<DraftCommentDataModel>())
                .FirstOrDefault(c => string.Equals(c.Id, commentId, StringComparison.OrdinalIgnoreCase));
            if (draftComment is not null)
                return Task.FromResult(draftComment.IsComplete);
        }

        if (_redReviewsForQuestions.TryGetValue(questionId, out var review))
        {
            review.Comments ??= new List<DraftCommentDataModel>();
            var reviewComment = review.Comments
                .FirstOrDefault(c => string.Equals(c.Id, commentId, StringComparison.OrdinalIgnoreCase));
            if (reviewComment is not null)
                return Task.FromResult(reviewComment.IsComplete);
        }

        if (_finalAnswersForQuestions.TryGetValue(questionId, out var answer))
        {
            answer.Comments ??= new List<DraftCommentDataModel>();
            var answerComment = answer.Comments
                .FirstOrDefault(c => string.Equals(c.Id, commentId, StringComparison.OrdinalIgnoreCase));
            if (answerComment is not null)
                return Task.FromResult(answerComment.IsComplete);
        }

        return Task.FromResult(false);
    }

    public Task<RedReviewDataModel?> GetRedReview(string bidId, string questionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(questionId))
            return Task.FromResult<RedReviewDataModel?>(null);

        if (_redReviewsForQuestions.TryGetValue(questionId, out var review))
        {
            return Task.FromResult<RedReviewDataModel?>(new RedReviewDataModel
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
            });
        }

        return Task.FromResult<RedReviewDataModel?>(null);
    }

    public Task SetRedReview(string bidId, string questionId, RedReviewDataModel review, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(questionId))
            return Task.CompletedTask;

        var toStore = new RedReviewDataModel
        {
            QuestionId = questionId,
            ResultText = review.ResultText ?? string.Empty,
            State = review.State,
            Comments = (_redReviewsForQuestions.TryGetValue(questionId, out var existing)
                ? existing.Comments
                : new List<DraftCommentDataModel>())
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

        _redReviewsForQuestions[questionId] = toStore;
        return Task.CompletedTask;
    }

    public Task<FinalAnswerDataModel?> GetFinalAnswer(string bidId, string questionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(questionId))
            return Task.FromResult<FinalAnswerDataModel?>(null);

        if (_finalAnswersForQuestions.TryGetValue(questionId, out var answer))
        {
            return Task.FromResult<FinalAnswerDataModel?>(new FinalAnswerDataModel
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
            });
        }

        return Task.FromResult<FinalAnswerDataModel?>(null);
    }

    public Task SetFinalAnswer(string bidId, string questionId, FinalAnswerDataModel answer, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(questionId))
            return Task.CompletedTask;

        _finalAnswersForQuestions[questionId] = new FinalAnswerDataModel
        {
            QuestionId = questionId,
            AnswerText = answer.AnswerText ?? string.Empty,
            ReadyForSubmission = answer.ReadyForSubmission,
            Comments = (_finalAnswersForQuestions.TryGetValue(questionId, out var existing)
                ? existing.Comments
                : new List<DraftCommentDataModel>())
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

        return Task.CompletedTask;
    }

    public Task<DraftCommentDataModel> AddRedReviewComment(
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

        if (!_redReviewsForQuestions.TryGetValue(questionId, out var review))
        {
            review = new RedReviewDataModel { QuestionId = questionId };
            _redReviewsForQuestions[questionId] = review;
        }

        review.Comments ??= new List<DraftCommentDataModel>();
        var newComment = CreateInlineComment(comment, userId, authorName, startIndex, endIndex, selectedText);
        review.Comments.Add(newComment);
        return Task.FromResult(newComment);
    }

    public Task<DraftCommentDataModel> AddFinalAnswerComment(
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

        if (!_finalAnswersForQuestions.TryGetValue(questionId, out var answer))
        {
            answer = new FinalAnswerDataModel { QuestionId = questionId };
            _finalAnswersForQuestions[questionId] = answer;
        }

        answer.Comments ??= new List<DraftCommentDataModel>();
        var newComment = CreateInlineComment(comment, userId, authorName, startIndex, endIndex, selectedText);
        answer.Comments.Add(newComment);
        return Task.FromResult(newComment);
    }

    public Task<DraftCommentDataModel> SetRedReviewCommentCompletion(
        string bidId,
        string questionId,
        string commentId,
        bool isComplete,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(questionId))
            throw new Exception("Invalid request, questionId is null or empty");

        if (!_redReviewsForQuestions.TryGetValue(questionId, out var review))
            throw new Exception("Invalid request, red review does not exist");

        review.Comments ??= new List<DraftCommentDataModel>();
        var comment = review.Comments.FirstOrDefault(c => string.Equals(c.Id, commentId, StringComparison.OrdinalIgnoreCase))
                      ?? throw new Exception("Invalid request, commentId does not exist");
        comment.IsComplete = isComplete;
        return Task.FromResult(comment);
    }

    public Task<DraftCommentDataModel> SetFinalAnswerCommentCompletion(
        string bidId,
        string questionId,
        string commentId,
        bool isComplete,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(questionId))
            throw new Exception("Invalid request, questionId is null or empty");

        if (!_finalAnswersForQuestions.TryGetValue(questionId, out var answer))
            throw new Exception("Invalid request, final answer does not exist");

        answer.Comments ??= new List<DraftCommentDataModel>();
        var comment = answer.Comments.FirstOrDefault(c => string.Equals(c.Id, commentId, StringComparison.OrdinalIgnoreCase))
                      ?? throw new Exception("Invalid request, commentId does not exist");
        comment.IsComplete = isComplete;
        return Task.FromResult(comment);
    }

    private DraftDataModel GetDraftOrThrow(string questionId, string draftId)
    {
        if (string.IsNullOrWhiteSpace(questionId) || string.IsNullOrWhiteSpace(draftId))
            throw new Exception("Invalid request, questionId or draftId is null or empty");

        if (!_draftsForQuestions.TryGetValue(questionId, out var responses))
            throw new Exception("Invalid request, questionId does not exist");

        var draft = responses.FirstOrDefault(x => x.Id == draftId);
        if (draft is null)
            throw new Exception("Invalid request, draftId does not exist");

        return draft;
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

    private void SetAllCommentsCompleteForBid(BidDataModel bid)
    {
        if (bid.Questions is null || bid.Questions.Count == 0)
            return;

        foreach (var question in bid.Questions)
        {
            if (string.IsNullOrWhiteSpace(question.Id))
                continue;

            if (_draftsForQuestions.TryGetValue(question.Id, out var drafts))
            {
                foreach (var draft in drafts)
                    SetCommentsComplete(draft.Comments);
            }

            if (_redReviewsForQuestions.TryGetValue(question.Id, out var review))
                SetCommentsComplete(review.Comments);

            if (_finalAnswersForQuestions.TryGetValue(question.Id, out var answer))
                SetCommentsComplete(answer.Comments);
        }
    }

    private static void SetCommentsComplete(List<DraftCommentDataModel>? comments)
    {
        if (comments is null || comments.Count == 0)
            return;

        foreach (var comment in comments)
            comment.IsComplete = true;
    }

    private static string BuildChatThreadKey(string bidId, string questionId, string userId)
        => $"{bidId}|{questionId}|{userId}";

    private static DocumentIngestionJobDataModel CloneDocumentIngestionJob(DocumentIngestionJobDataModel source)
    {
        return new DocumentIngestionJobDataModel
        {
            JobId = source.JobId,
            OwnerUserKey = source.OwnerUserKey,
            FileName = source.FileName,
            Stage = source.Stage,
            Status = source.Status,
            Message = source.Message,
            IsComplete = source.IsComplete,
            IsError = source.IsError,
            CreatedAtUtc = source.CreatedAtUtc,
            UpdatedAtUtc = source.UpdatedAtUtc,
            CompletedAtUtc = source.CompletedAtUtc,
            Result = source.Result is null
                ? null
                : new ParsedDocumentResponse
                {
                    UniqueReference = source.Result.UniqueReference,
                    Company = source.Result.Company,
                    Summary = source.Result.Summary,
                    KeyInformation = source.Result.KeyInformation,
                    Budget = source.Result.Budget,
                    DeadlineForQualifying = source.Result.DeadlineForQualifying,
                    DeadlineForSubmission = source.Result.DeadlineForSubmission,
                    LengthOfContract = source.Result.LengthOfContract,
                    Questions = source.Result.Questions?
                        .Select(q => new ParsedQuestionResponse
                        {
                            QuestionOrderIndex = q.QuestionOrderIndex,
                            Category = q.Category,
                            Number = q.Number,
                            Title = q.Title,
                            Description = q.Description,
                            Length = q.Length,
                            Weighting = q.Weighting,
                            Required = q.Required,
                            NiceToHave = q.NiceToHave
                        })
                        .ToList() ?? []
                }
        };
    }
}
