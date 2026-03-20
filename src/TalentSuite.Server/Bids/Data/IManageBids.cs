using TalentSuite.Server.Bids.Data.Models;
using TalentSuite.Shared.Bids;

namespace TalentSuite.Server.Bids.Data;

public interface IManageBids
{
    Task<Guid> StoreBid(BidDataModel request, CancellationToken ct = default);

    Task<BidDataModel> GetBid(string id, CancellationToken ct = default);

    Task<SearchDataModel> SearchBids(int page, int pageSize, CancellationToken ct = default);
    
    Task<List<string>> GetBidUsers(string bidId, CancellationToken ct = default);

    Task AddBidUser(string bidId, string userId, CancellationToken ct = default);

    Task RemoveBidUser(string bidId, string userId, CancellationToken ct = default);

    Task<List<BidFileDataModel>> GetBidFiles(string bidId, CancellationToken ct = default);

    Task<BidFileDataModel> AddBidFile(
        string bidId,
        string fileName,
        string contentType,
        byte[] content,
        CancellationToken ct = default);

    Task<BidFileDataModel?> GetBidFile(string bidId, string fileId, CancellationToken ct = default);

    Task<bool> DeleteBidFile(string bidId, string fileId, CancellationToken ct = default);

    Task<string?> GetChatThreadId(string bidId, string questionId, string userId, CancellationToken ct = default);

    Task SetChatThreadId(string bidId, string questionId, string userId, string threadId, CancellationToken ct = default);

    Task SetBidStatus(string bidId, BidStatus status, CancellationToken ct = default);

    Task<BidLibraryPushDataModel> PushBidToLibrary(
        string bidId,
        string performedByUserId,
        string performedByName,
        DateTime pushedAtUtc,
        CancellationToken ct = default);
    
    Task<List<QuestionAssignmentDataModel>> GetBidQuestionUsers(string bidId, string questionId, CancellationToken ct = default);

    Task AddBidQuestionUser(string bidId, string questionId, string userId, QuestionUserRole role, CancellationToken ct = default);

    Task UpdateBidQuestionUserRole(string bidId, string questionId, string userId, QuestionUserRole role, CancellationToken ct = default);
    
    Task<QuestionDataModel> GetQuestion(string bidId, string questionId, CancellationToken ct = default);
    
    Task RemoveBidQuestionUser(string bidId, string questionId, string userId, CancellationToken ct = default);
    
    Task<string> AddQuestionDraft(string bidId, string questionId, string response, CancellationToken ct = default);

    Task RemoveQuestionDraft(string bidId, string questionId, string responseId, CancellationToken ct = default);

    Task<List<DraftDataModel>> GetQuestionDrafts(string bidId, string questionId, CancellationToken ct = default);
    
    Task UpdateQuestionDraft(string bidId, string questionId, string draftId, string draft, CancellationToken ct);

    Task<DraftCommentDataModel> AddQuestionDraftComment(
        string bidId,
        string questionId,
        string draftId,
        string comment,
        string userId,
        string authorName,
        int? startIndex,
        int? endIndex,
        string selectedText,
        CancellationToken ct = default);

    Task CreateMentionTasks(
        string bidId,
        string questionId,
        string commentId,
        string commentText,
        List<string> mentionedUserIds,
        CancellationToken ct = default);

    Task<List<MentionTaskDataModel>> GetMentionTasksForUser(string userId, CancellationToken ct = default);
    Task<List<AssignedQuestionDataModel>> GetAssignedQuestionsForUser(string userId, CancellationToken ct = default);

    Task<bool> IsCommentComplete(string bidId, string questionId, string commentId, CancellationToken ct = default);

    Task<List<DraftCommentDataModel>> GetQuestionDraftComments(string bidId, string questionId, string draftId, CancellationToken ct = default);

    Task<DraftCommentDataModel> SetQuestionDraftCommentCompletion(
        string bidId,
        string questionId,
        string draftId,
        string commentId,
        bool isComplete,
        CancellationToken ct = default);

    Task<RedReviewDataModel?> GetRedReview(string bidId, string questionId, CancellationToken ct = default);

    Task SetRedReview(string bidId, string questionId, RedReviewDataModel review, CancellationToken ct = default);

    Task<FinalAnswerDataModel?> GetFinalAnswer(string bidId, string questionId, CancellationToken ct = default);

    Task SetFinalAnswer(string bidId, string questionId, FinalAnswerDataModel answer, CancellationToken ct = default);

    Task<DraftCommentDataModel> AddRedReviewComment(
        string bidId,
        string questionId,
        string comment,
        string userId,
        string authorName,
        int? startIndex,
        int? endIndex,
        string selectedText,
        CancellationToken ct = default);

    Task<DraftCommentDataModel> SetRedReviewCommentCompletion(
        string bidId,
        string questionId,
        string commentId,
        bool isComplete,
        CancellationToken ct = default);

    Task<DraftCommentDataModel> AddFinalAnswerComment(
        string bidId,
        string questionId,
        string comment,
        string userId,
        string authorName,
        int? startIndex,
        int? endIndex,
        string selectedText,
        CancellationToken ct = default);

    Task<DraftCommentDataModel> SetFinalAnswerCommentCompletion(
        string bidId,
        string questionId,
        string commentId,
        bool isComplete,
        CancellationToken ct = default);
}
