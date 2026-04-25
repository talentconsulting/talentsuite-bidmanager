using TalentSuite.Server.Bids.Data;
using TalentSuite.Server.Bids.Data.Models;
using TalentSuite.Server.Bids.Mappers;
using TalentSuite.Server.Bids.Services.Models;
using TalentSuite.Shared.Bids;
using TalentSuite.Shared.Messaging;
using TalentSuite.Shared.Messaging.Events;
using TalentSuite.Shared.Tasks;
using TalentSuite.Server.Users.Data;
using TalentSuite.Shared.Bids.Ai;

namespace TalentSuite.Server.Bids.Services;

public interface IBidService
{
    Task<ParsedDocumentModel> ParseBidDocument(Stream stream, string filename, BidStage stage, CancellationToken ct = default);

    Task<Guid> CreateBid(CreateBidModel request, CancellationToken ct = default);

    Task<BidModel> GetBid(string id, CancellationToken ct = default);

    Task<PagedBidListModel> SearchBids(int page, int pageSize, CancellationToken ct = default);
    
    Task<List<string>> GetBidUsers(string bidId, CancellationToken ct = default);
    
    Task AddBidUser(string bidId, string userId, CancellationToken ct = default);
    
    Task RemoveBidUser(string bidId, string userId, CancellationToken ct = default);

    Task<List<BidFileResponse>> GetBidFiles(string bidId, CancellationToken ct = default);

    Task<BidFileResponse> AddBidFile(
        string bidId,
        string fileName,
        string contentType,
        byte[] content,
        CancellationToken ct = default);

    Task<BidFileContentModel?> GetBidFile(string bidId, string fileId, CancellationToken ct = default);

    Task<bool> DeleteBidFile(string bidId, string fileId, CancellationToken ct = default);

    Task<string?> GetChatThreadId(string bidId, string questionId, string userId, CancellationToken ct = default);

    Task SetChatThreadId(string bidId, string questionId, string userId, string threadId, CancellationToken ct = default);

    Task<List<ChatMessageResponse>> GetChatMessages(string bidId, string questionId, string userId, CancellationToken ct = default);

    Task AddChatMessage(
        string bidId,
        string questionId,
        string userId,
        string role,
        string content,
        DateTimeOffset createdAtUtc,
        CancellationToken ct = default);

    Task SetBidStatus(string bidId, BidStatus status, CancellationToken ct = default);
    Task<BidLibraryPushResponse> PushBidToLibrary(
        string bidId,
        string performedByUserId,
        string performedByName,
        DateTime pushedAtUtc,
        CancellationToken ct = default);

    Task<List<QuestionAssignmentResponse>> GetBidQuestionUsers(string bidId, string questionId, CancellationToken ct = default);
    
    Task AddBidQuestionUser(string bidId, string questionId, string userId, QuestionUserRole role, CancellationToken ct = default);

    Task UpdateBidQuestionUserRole(string bidId, string questionId, string userId, QuestionUserRole role, CancellationToken ct = default);
    
    Task RemoveBidQuestionUser(string bidId, string questionId, string userId, CancellationToken ct = default);
    
    Task<string> AddDraft(string bidId, string questionId, string response, CancellationToken ct = default);

    Task UpdateDraft(string bidId, string questionId, string draftId, string draft, CancellationToken ct = default);

    Task RemoveDraft(string bidId, string questionId, string responseId, CancellationToken ct = default);

    Task<List<DraftModel>> GetQuestionDrafts(string bidId, string questionId, CancellationToken ct = default);

    Task<DraftCommentModel> AddDraftComment(
        string bidId,
        string questionId,
        string draftId,
        string comment,
        string userId,
        string authorName,
        List<string> mentionedUserIds,
        int? startIndex,
        int? endIndex,
        string selectedText,
        CancellationToken ct = default);

    Task<List<DraftCommentModel>> GetDraftComments(string bidId, string questionId, string draftId, CancellationToken ct = default);

    Task<DraftCommentResponse> SetDraftCommentCompletion(
        string bidId,
        string questionId,
        string draftId,
        string commentId,
        bool isComplete,
        CancellationToken ct = default);
    
    Task<CreateQuestionModel> GetQuestion(string bidId, string questionId, CancellationToken ct = default);

    Task<RedReviewResponse?> GetRedReview(string bidId, string questionId, CancellationToken ct = default);

    Task SetRedReview(string bidId, string questionId, UpdateRedReviewRequest request, CancellationToken ct = default);

    Task<FinalAnswerResponse?> GetFinalAnswer(string bidId, string questionId, CancellationToken ct = default);

    Task SetFinalAnswer(string bidId, string questionId, UpdateFinalAnswerRequest request, CancellationToken ct = default);

    Task<DraftCommentResponse> AddRedReviewComment(string bidId, string questionId, AddDraftCommentRequest request, CancellationToken ct = default);

    Task<DraftCommentResponse> SetRedReviewCommentCompletion(
        string bidId,
        string questionId,
        string commentId,
        bool isComplete,
        CancellationToken ct = default);

    Task<DraftCommentResponse> AddFinalAnswerComment(string bidId, string questionId, AddDraftCommentRequest request, CancellationToken ct = default);

    Task<DraftCommentResponse> SetFinalAnswerCommentCompletion(
        string bidId,
        string questionId,
        string commentId,
        bool isComplete,
        CancellationToken ct = default);

    Task<List<MentionTaskResponse>> GetMentionTasksForUser(string userId, CancellationToken ct = default);
    Task<List<AssignedQuestionTaskResponse>> GetAssignedQuestionsForUser(string userId, CancellationToken ct = default);
}

public sealed class BidService : IBidService
{
    private const string CommentSavedWithMentionsEntityDefault = "comment-saved-with-mentions";
    private readonly IDocumentIngestionservice _documentExtractor;
    private readonly IManageBids _repository;
    private readonly BidMapper _mapper;
    private readonly IManageUsers _users;
    private readonly IAzureServiceBusClient _serviceBusClient;
    private readonly string _commentSavedWithMentionsEntityName;
    private readonly string _frontendBaseUrl;

    public BidService(
        IDocumentIngestionservice documentExtractor,
        IManageBids repository,
        BidMapper mapper,
        IManageUsers users,
        IAzureServiceBusClient serviceBusClient,
        IConfiguration configuration)
    {
        _documentExtractor = documentExtractor ?? throw new ArgumentNullException(nameof(documentExtractor));
        _repository = repository;
        _mapper = mapper;
        _users = users;
        _serviceBusClient = serviceBusClient;
        _commentSavedWithMentionsEntityName = configuration["AzureServiceBus:CommentSavedWithMentionsEntityName"]
                                              ?? CommentSavedWithMentionsEntityDefault;
        _frontendBaseUrl = ResolveFrontendBaseUrl(configuration);
    }

    private static string ResolveFrontendBaseUrl(IConfiguration configuration)
    {
        var candidates = new[]
        {
            configuration["FRONTEND_PUBLIC_ORIGIN"],
            configuration["TALENTFRONTEND_HTTPS"],
            configuration["InviteEmail:FrontendBaseUrl"],
            configuration["InviteEmail__FrontendBaseUrl"]
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            var trimmed = candidate.Trim();
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
                continue;

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var authority = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(authority))
                return authority;
        }

        return string.Empty;
    }

    public async Task<ParsedDocumentModel> ParseBidDocument(Stream stream, string filename, BidStage stage, CancellationToken ct = default)
    {
        if (stream.CanSeek)
            stream.Position = 0;

        return await _documentExtractor.ExtractDocumentAsync(
            documentStream: stream,
            filename: filename,
            stage: stage,
            ct: ct);
    }

    public async Task<Guid> CreateBid(CreateBidModel request, CancellationToken ct = default)
    {
        var dataModel = _mapper.ToDataModel(request);
        
        return await _repository.StoreBid(dataModel);
    }

    public async Task<BidModel> GetBid(string id, CancellationToken ct)
    {
        var model = await _repository.GetBid(id);

        return _mapper.ToModel(model);
    }

    public async Task<PagedBidListModel> SearchBids(int page, int pageSize, CancellationToken ct = default)
    {
        var dataModel = await _repository.SearchBids(page, pageSize, ct);

        var response = _mapper.ToModel(dataModel);
        
        return response;
    }

    public async Task<List<string>> GetBidUsers(string bidId, CancellationToken ct = default)
    {
        return await _repository.GetBidUsers(bidId, ct);
    }

    public async Task AddBidUser(string bidId, string userId, CancellationToken ct = default)
    {
        await _repository.AddBidUser(bidId, userId, ct);
    }

    public async Task RemoveBidUser(string bidId, string userId, CancellationToken ct = default)
    {
        await _repository.RemoveBidUser(bidId, userId, ct);
    }

    public async Task<List<BidFileResponse>> GetBidFiles(string bidId, CancellationToken ct = default)
    {
        var files = await _repository.GetBidFiles(bidId, ct);
        return files
            .OrderByDescending(x => x.UploadedAtUtc)
            .Select(x => new BidFileResponse
            {
                Id = x.Id,
                BidId = x.BidId,
                FileName = x.FileName,
                ContentType = x.ContentType,
                SizeBytes = x.SizeBytes,
                UploadedAtUtc = x.UploadedAtUtc
            })
            .ToList();
    }

    public async Task<BidFileResponse> AddBidFile(
        string bidId,
        string fileName,
        string contentType,
        byte[] content,
        CancellationToken ct = default)
    {
        var added = await _repository.AddBidFile(bidId, fileName, contentType, content, ct);
        return new BidFileResponse
        {
            Id = added.Id,
            BidId = added.BidId,
            FileName = added.FileName,
            ContentType = added.ContentType,
            SizeBytes = added.SizeBytes,
            UploadedAtUtc = added.UploadedAtUtc
        };
    }

    public async Task<BidFileContentModel?> GetBidFile(string bidId, string fileId, CancellationToken ct = default)
    {
        var file = await _repository.GetBidFile(bidId, fileId, ct);
        if (file is null)
            return null;

        return new BidFileContentModel
        {
            FileName = file.FileName,
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            Content = file.Content ?? Array.Empty<byte>()
        };
    }

    public Task<bool> DeleteBidFile(string bidId, string fileId, CancellationToken ct = default)
        => _repository.DeleteBidFile(bidId, fileId, ct);

    public Task<string?> GetChatThreadId(string bidId, string questionId, string userId, CancellationToken ct = default)
        => _repository.GetChatThreadId(bidId, questionId, userId, ct);

    public Task SetChatThreadId(string bidId, string questionId, string userId, string threadId, CancellationToken ct = default)
        => _repository.SetChatThreadId(bidId, questionId, userId, threadId, ct);

    public async Task<List<ChatMessageResponse>> GetChatMessages(string bidId, string questionId, string userId, CancellationToken ct = default)
    {
        var result = await _repository.GetChatMessages(bidId, questionId, userId, ct);
        return result
            .OrderBy(message => message.CreatedAtUtc)
            .ThenBy(message => message.Id, StringComparer.OrdinalIgnoreCase)
            .Select(message => new ChatMessageResponse
            {
                Id = message.Id,
                Role = message.Role,
                Content = message.Content,
                CreatedAtUtc = message.CreatedAtUtc
            })
            .ToList();
    }

    public Task AddChatMessage(
        string bidId,
        string questionId,
        string userId,
        string role,
        string content,
        DateTimeOffset createdAtUtc,
        CancellationToken ct = default)
        => _repository.AddChatMessage(bidId, questionId, userId, role, content, createdAtUtc, ct);

    public async Task SetBidStatus(string bidId, BidStatus status, CancellationToken ct = default)
    {
        await _repository.SetBidStatus(bidId, status, ct);
    }

    public async Task<BidLibraryPushResponse> PushBidToLibrary(
        string bidId,
        string performedByUserId,
        string performedByName,
        DateTime pushedAtUtc,
        CancellationToken ct = default)
    {
        var result = await _repository.PushBidToLibrary(
            bidId,
            performedByUserId,
            performedByName,
            pushedAtUtc,
            ct);

        return new BidLibraryPushResponse
        {
            BidId = result.BidId,
            PerformedByUserId = result.PerformedByUserId,
            PerformedByName = result.PerformedByName,
            PushedAtUtc = result.PushedAtUtc
        };
    }

    public async Task<List<QuestionAssignmentResponse>> GetBidQuestionUsers(string bidId, string questionId, CancellationToken ct = default)
    {
        var result = await _repository.GetBidQuestionUsers(bidId, questionId, ct);
        return result
            .Select(x => new QuestionAssignmentResponse
            {
                UserId = x.UserId,
                Role = x.Role
            })
            .ToList();
    }

    public async Task AddBidQuestionUser(string bidId, string questionId, string userId, QuestionUserRole role, CancellationToken ct = default)
    {
        await _repository.AddBidQuestionUser(bidId, questionId, userId, role, ct);
    }

    public async Task UpdateBidQuestionUserRole(string bidId, string questionId, string userId, QuestionUserRole role, CancellationToken ct = default)
    {
        await _repository.UpdateBidQuestionUserRole(bidId, questionId, userId, role, ct);
    }

    public async Task RemoveBidQuestionUser(string bidId, string questionId, string userId, CancellationToken ct = default)
    {
        await _repository.RemoveBidQuestionUser(bidId, questionId, userId, ct);
    }

    public async Task<string> AddDraft(string bidId, string questionId, string response,
        CancellationToken ct = default)
    {
        return await _repository.AddQuestionDraft(bidId, questionId, response, ct);
    }

    public async Task UpdateDraft(string bidId, string questionId, string draftId, string draft, CancellationToken ct = default)
    {
        await _repository.UpdateQuestionDraft(bidId, questionId, draftId, draft, ct);
    }

    public async Task RemoveDraft(string bidId, string questionId, string responseId, CancellationToken ct = default)
    {
        await _repository.RemoveQuestionDraft(bidId, questionId, responseId, ct);
    }

    public async Task<List<DraftModel>> GetQuestionDrafts(string bidId, string questionId, CancellationToken ct = default)
    {
        var dataModel = await _repository.GetQuestionDrafts(bidId, questionId, ct);

        return _mapper.ToDraftModels(dataModel);
    }

    public async Task<DraftCommentModel> AddDraftComment(
        string bidId,
        string questionId,
        string draftId,
        string comment,
        string userId,
        string authorName,
        List<string> mentionedUserIds,
        int? startIndex,
        int? endIndex,
        string selectedText,
        CancellationToken ct = default)
    {
        var dataModel = await _repository.AddQuestionDraftComment(
            bidId,
            questionId,
            draftId,
            comment,
            userId,
            authorName,
            startIndex,
            endIndex,
            selectedText,
            ct);
        await _repository.CreateMentionTasks(
            bidId,
            questionId,
            dataModel.Id,
            dataModel.Comment,
            mentionedUserIds,
            ct);
        await PublishCommentSavedWithMentionsEventAsync(
            bidId,
            questionId,
            dataModel.Id,
            dataModel.Comment,
            dataModel.SelectedText ?? string.Empty,
            "drafts",
            mentionedUserIds,
            ct);
        return _mapper.ToModel(dataModel);
    }

    public async Task<List<DraftCommentModel>> GetDraftComments(string bidId, string questionId, string draftId, CancellationToken ct = default)
    {
        var dataModel = await _repository.GetQuestionDraftComments(bidId, questionId, draftId, ct);
        return _mapper.ToDraftCommentModels(dataModel);
    }

    public async Task<DraftCommentResponse> SetDraftCommentCompletion(
        string bidId,
        string questionId,
        string draftId,
        string commentId,
        bool isComplete,
        CancellationToken ct = default)
    {
        var result = await _repository.SetQuestionDraftCommentCompletion(
            bidId,
            questionId,
            draftId,
            commentId,
            isComplete,
            ct);

        return _mapper.ToResponse(result);
    }

    public async Task<CreateQuestionModel> GetQuestion(string bidId, string questionId, CancellationToken ct = default)
    {
        var dataModel = await _repository.GetQuestion(bidId, questionId, ct);

        return _mapper.ToModel(dataModel);
    }

    public async Task<RedReviewResponse?> GetRedReview(string bidId, string questionId, CancellationToken ct = default)
    {
        var dataModel = await _repository.GetRedReview(bidId, questionId, ct);
        if (dataModel is null)
            return null;

        return new RedReviewResponse
        {
            QuestionId = dataModel.QuestionId,
            ResultText = dataModel.ResultText,
            State = dataModel.State,
            Comments = dataModel.Comments
                .Select(c => new DraftCommentResponse
                {
                    Id = c.Id,
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
            Reviewers = dataModel.Reviewers
                .Select(r => new RedReviewReviewerResponse
                {
                    UserId = r.UserId,
                    State = r.State
                })
                .ToList()
        };
    }

    public async Task SetRedReview(string bidId, string questionId, UpdateRedReviewRequest request, CancellationToken ct = default)
    {
        var dataModel = new RedReviewDataModel
        {
            QuestionId = questionId,
            ResultText = request.ResultText ?? string.Empty,
            State = request.State,
            Reviewers = request.Reviewers
                .Select(r => new RedReviewReviewerDataModel
                {
                    UserId = r.UserId,
                    State = r.State
                })
                .ToList()
        };

        await _repository.SetRedReview(bidId, questionId, dataModel, ct);
    }

    public async Task<FinalAnswerResponse?> GetFinalAnswer(string bidId, string questionId, CancellationToken ct = default)
    {
        var dataModel = await _repository.GetFinalAnswer(bidId, questionId, ct);
        if (dataModel is null)
            return null;

        return new FinalAnswerResponse
        {
            QuestionId = dataModel.QuestionId,
            AnswerText = dataModel.AnswerText,
            ReadyForSubmission = dataModel.ReadyForSubmission,
            Comments = dataModel.Comments
                .Select(c => new DraftCommentResponse
                {
                    Id = c.Id,
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

    public async Task SetFinalAnswer(string bidId, string questionId, UpdateFinalAnswerRequest request, CancellationToken ct = default)
    {
        var dataModel = new FinalAnswerDataModel
        {
            QuestionId = questionId,
            AnswerText = request.AnswerText ?? string.Empty,
            ReadyForSubmission = request.ReadyForSubmission
        };

        await _repository.SetFinalAnswer(bidId, questionId, dataModel, ct);
    }

    public async Task<DraftCommentResponse> AddRedReviewComment(string bidId, string questionId, AddDraftCommentRequest request, CancellationToken ct = default)
    {
        var result = await _repository.AddRedReviewComment(
            bidId,
            questionId,
            request.Comment?.Trim() ?? string.Empty,
            request.UserId?.Trim() ?? string.Empty,
            request.AuthorName?.Trim() ?? string.Empty,
            request.StartIndex,
            request.EndIndex,
            request.SelectedText ?? string.Empty,
            ct);
        await _repository.CreateMentionTasks(
            bidId,
            questionId,
            result.Id,
            result.Comment,
            request.MentionedUserIds ?? new List<string>(),
            ct);
        await PublishCommentSavedWithMentionsEventAsync(
            bidId,
            questionId,
            result.Id,
            result.Comment,
            result.SelectedText ?? string.Empty,
            "review",
            request.MentionedUserIds,
            ct);

        return new DraftCommentResponse
        {
            Id = result.Id,
            Comment = result.Comment,
            IsComplete = result.IsComplete,
            UserId = result.UserId,
            AuthorName = result.AuthorName,
            CreatedAtUtc = result.CreatedAtUtc,
            StartIndex = result.StartIndex,
            EndIndex = result.EndIndex,
            SelectedText = result.SelectedText
        };
    }

    public async Task<DraftCommentResponse> SetRedReviewCommentCompletion(
        string bidId,
        string questionId,
        string commentId,
        bool isComplete,
        CancellationToken ct = default)
    {
        var result = await _repository.SetRedReviewCommentCompletion(bidId, questionId, commentId, isComplete, ct);
        return _mapper.ToResponse(result);
    }

    public async Task<DraftCommentResponse> AddFinalAnswerComment(string bidId, string questionId, AddDraftCommentRequest request, CancellationToken ct = default)
    {
        var result = await _repository.AddFinalAnswerComment(
            bidId,
            questionId,
            request.Comment?.Trim() ?? string.Empty,
            request.UserId?.Trim() ?? string.Empty,
            request.AuthorName?.Trim() ?? string.Empty,
            request.StartIndex,
            request.EndIndex,
            request.SelectedText ?? string.Empty,
            ct);
        await _repository.CreateMentionTasks(
            bidId,
            questionId,
            result.Id,
            result.Comment,
            request.MentionedUserIds ?? new List<string>(),
            ct);
        await PublishCommentSavedWithMentionsEventAsync(
            bidId,
            questionId,
            result.Id,
            result.Comment,
            result.SelectedText ?? string.Empty,
            "final-answer",
            request.MentionedUserIds,
            ct);

        return new DraftCommentResponse
        {
            Id = result.Id,
            Comment = result.Comment,
            IsComplete = result.IsComplete,
            UserId = result.UserId,
            AuthorName = result.AuthorName,
            CreatedAtUtc = result.CreatedAtUtc,
            StartIndex = result.StartIndex,
            EndIndex = result.EndIndex,
            SelectedText = result.SelectedText
        };
    }

    public async Task<DraftCommentResponse> SetFinalAnswerCommentCompletion(
        string bidId,
        string questionId,
        string commentId,
        bool isComplete,
        CancellationToken ct = default)
    {
        var result = await _repository.SetFinalAnswerCommentCompletion(bidId, questionId, commentId, isComplete, ct);
        return _mapper.ToResponse(result);
    }

    public async Task<List<MentionTaskResponse>> GetMentionTasksForUser(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return new List<MentionTaskResponse>();

        var tasks = await _repository.GetMentionTasksForUser(userId, ct);
        var openTasks = new List<MentionTaskDataModel>(tasks.Count);
        foreach (var task in tasks)
        {
            if (string.IsNullOrWhiteSpace(task.CommentId))
            {
                openTasks.Add(task);
                continue;
            }

            var isComplete = await _repository.IsCommentComplete(task.BidId, task.QuestionId, task.CommentId, ct);
            if (!isComplete)
                openTasks.Add(task);
        }

        var bidTitlesById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var questionTitlesByBidAndQuestionId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var bidId in openTasks
                     .Select(task => task.BidId)
                     .Where(id => !string.IsNullOrWhiteSpace(id))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var bid = await _repository.GetBid(bidId, ct);
                bidTitlesById[bidId] = string.IsNullOrWhiteSpace(bid.Company) ? bidId : bid.Company;

                foreach (var question in bid.Questions)
                {
                    if (string.IsNullOrWhiteSpace(question?.Id))
                        continue;

                    questionTitlesByBidAndQuestionId[$"{bidId}::{question.Id}"] =
                        string.IsNullOrWhiteSpace(question.Title)
                            ? question.Id
                            : $"{question.Number} - {question.Title}";
                }
            }
            catch
            {
                bidTitlesById[bidId] = bidId;
            }
        }

        return openTasks.Select(t => new MentionTaskResponse
        {
            Id = t.Id,
            BidId = t.BidId,
            BidTitle = bidTitlesById.TryGetValue(t.BidId, out var bidTitle) ? bidTitle : t.BidId,
            QuestionId = t.QuestionId,
            QuestionTitle = questionTitlesByBidAndQuestionId.TryGetValue($"{t.BidId}::{t.QuestionId}", out var questionTitle)
                ? questionTitle
                : t.QuestionId,
            CommentId = t.CommentId,
            AssignedUserId = t.AssignedUserId,
            CommentText = t.CommentText,
            CreatedAtUtc = t.CreatedAtUtc
        }).ToList();
    }

    public async Task<List<AssignedQuestionTaskResponse>> GetAssignedQuestionsForUser(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return new List<AssignedQuestionTaskResponse>();

        var assigned = await _repository.GetAssignedQuestionsForUser(userId, ct);
        return assigned
            .Select(x => new AssignedQuestionTaskResponse
            {
                BidId = x.BidId,
                BidTitle = x.BidTitle,
                QuestionId = x.QuestionId,
                QuestionTitle = x.QuestionTitle,
                Role = x.Role
            })
            .ToList();
    }

    private async Task PublishCommentSavedWithMentionsEventAsync(
        string bidId,
        string questionId,
        string commentId,
        string comment,
        string selectedText,
        string tab,
        List<string>? mentionedUserIds,
        CancellationToken ct)
    {
        if (mentionedUserIds is null || mentionedUserIds.Count == 0)
            return;

        var distinctMentionedIds = mentionedUserIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (distinctMentionedIds.Count == 0)
            return;

        var users = await _users.GetUsers(ct);
        var mentionedUsers = users
            .Where(u => distinctMentionedIds.Contains(u.Id, StringComparer.OrdinalIgnoreCase))
            .Select(u => new CommentMentionedUser
            {
                FullName = u.Name ?? string.Empty,
                Email = u.Email ?? string.Empty
            })
            .Where(u => !string.IsNullOrWhiteSpace(u.Email) || !string.IsNullOrWhiteSpace(u.FullName))
            .ToList();

        if (mentionedUsers.Count == 0)
            return;

        var relativeLink =
            $"/bids/manage/{Uri.EscapeDataString(bidId)}?questionId={Uri.EscapeDataString(questionId)}&tab={Uri.EscapeDataString(tab)}&commentId={Uri.EscapeDataString(commentId)}";
        var questionLink = string.IsNullOrWhiteSpace(_frontendBaseUrl)
            ? relativeLink
            : $"{_frontendBaseUrl}{relativeLink}";

        var payload = new CommentSavedWithMentionsEvent
        {
            BidId = bidId,
            QuestionId = questionId,
            CommentId = commentId,
            Tab = tab,
            Comment = comment ?? string.Empty,
            SelectedText = selectedText ?? string.Empty,
            QuestionLink = questionLink,
            MentionedUsers = mentionedUsers
        };

        await _serviceBusClient.PublishAsync(_commentSavedWithMentionsEntityName, payload, ct);
    }
}
