using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using TalentSuite.FrontEnd.Pages.Bids.Management;
using TalentSuite.FrontEnd.Pages.Bids.Management.Models;
using TalentSuite.FrontEnd.Services;
using TalentSuite.Shared.Bids;
using TalentSuite.Shared.Bids.Ai;
using TalentSuite.Shared.Users;

namespace TalentSuite.FrontEnd.Pages.Bids;

public partial class BidManage : ComponentBase
{
    [Parameter] public string BidId { get; set; } = "";

    [Inject] public HttpClient Http { get; set; } = default!;
    [Inject] public NavigationManager Nav { get; set; } = default!;
    [Inject] public IJSRuntime JS { get; set; } = default!;
    [Inject] public GlobalBannerState BannerState { get; set; } = default!;
    [Inject] public BidManageApiClient ApiClient { get; set; } = default!;

    protected bool IsLoading { get; set; }
    protected bool IsBusy { get; set; }
    protected string? ErrorText { get; set; }
    protected string? QuestionErrorText { get; set; }
    protected string? UsersErrorText { get; set; }
    protected bool IsUsersBusy { get; set; }
    protected bool IsDraftBusy { get; set; }
    protected bool IsFilesBusy { get; set; }
    protected string? DraftErrorText { get; set; }
    protected string? FilesErrorText { get; set; }
    protected bool ShowNewDraft { get; set; }
    protected string? NewDraftText { get; set; }
    protected string? ChatQuestionText { get; set; }
    protected Dictionary<string, string?> DraftCommentTextsByDraftId { get; } = new(StringComparer.OrdinalIgnoreCase);

    protected LeftNavSection LeftNav { get; set; } = LeftNavSection.Overview;

    protected string? ActiveCategory { get; set; }
    protected string? ActiveQuestionId { get; set; }
    protected InnerTab ActiveInnerTab { get; set; } = InnerTab.Users;

    protected BidManageModel? Bid { get; set; }
    protected bool IsAdminUser { get; set; }
    protected bool CanManageBidUsers => IsAdminUser;
    protected bool CanManageQuestionUsers => IsAdminUser;
    protected bool IsApiCallInProgress => IsLoading || IsBusy || IsUsersBusy || IsDraftBusy || IsFilesBusy;
    protected string BusyOverlayMessage
        => IsLoading ? "Loading bid..."
            : IsUsersBusy ? "Updating users..."
            : IsFilesBusy ? "Working with files..."
            : IsDraftBusy ? "Updating draft..."
            : "Working...";

    private async Task SetLoadingAsync()
    {
        IsLoading = true;
        await InvokeAsync(StateHasChanged);
        await Task.Yield();
    }

    private async Task SetBusyAsync()
    {
        IsBusy = true;
        await InvokeAsync(StateHasChanged);
        await Task.Yield();
    }

    private async Task SetUsersBusyAsync()
    {
        IsUsersBusy = true;
        await InvokeAsync(StateHasChanged);
        await Task.Yield();
    }

    private async Task SetDraftBusyAsync()
    {
        IsDraftBusy = true;
        await InvokeAsync(StateHasChanged);
        await Task.Yield();
    }

    private async Task SetFilesBusyAsync()
    {
        IsFilesBusy = true;
        await InvokeAsync(StateHasChanged);
        await Task.Yield();
    }

    protected List<UserOption> AvailableUsers { get; } = new();
    protected List<UserOption> AssignedUsers { get; } = new();
    protected List<UserOption> AllUsers { get; } = new();
    protected List<BidFileResponse> BidFiles { get; } = new();
    protected Dictionary<string, QuestionUserRole> PendingQuestionUserRoles { get; } = new(StringComparer.OrdinalIgnoreCase);
    private string? _targetQuestionId;
    private string? _targetCommentId;
    private bool _targetApplied;
    private string? _pendingScrollElementId;

    protected override async Task OnInitializedAsync()
    {
        ReadDeepLinkTargetFromQuery();
        await LoadBidAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (string.IsNullOrWhiteSpace(_pendingScrollElementId))
            return;

        var target = _pendingScrollElementId;
        _pendingScrollElementId = null;
        await JS.InvokeVoidAsync("bidManage.scrollToElementById", target);
    }

    protected async Task LoadBidAsync()
    {
        await SetLoadingAsync();
        ErrorText = null;

        try
        {
            await LoadAuthorisationAsync();
            Bid = await ApiClient.GetBidAsync(BidId);

            if (Bid is null)
            {
                ErrorText = "Bid returned empty.";
                return;
            }

            if (CanManageBidUsers)
                await LoadUsersAsync();
            await LoadAssignedUserIdsAsync();
            SyncAvailableUsers();
            if (CanManageQuestionUsers)
                await LoadQuestionUsersAsync();
            await LoadQuestionFinalAnswersAsync();
            await LoadBidFilesAsync();

            // Initialise selection
            var groups = CategoryGroups;
            if (groups.Count > 0)
            {
                ActiveCategory = groups[0].Key;

                var firstQ = QuestionsInCategory.FirstOrDefault();
                if (firstQ is not null)
                    ActiveQuestionId = firstQ.Id;
            }

            if (!CanManageQuestionUsers && ActiveInnerTab == InnerTab.Users)
                ActiveInnerTab = InnerTab.Draft;

            if (!CanManageBidUsers && LeftNav == LeftNavSection.Users)
                LeftNav = LeftNavSection.Overview;

            await ApplyDeepLinkTargetIfPresentAsync();
        }
        catch (Exception ex)
        {
            ErrorText = ex.ToString();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadAuthorisationAsync()
    {
        try
        {
            var auth = await ApiClient.GetMyAuthorisationAsync();
            IsAdminUser = auth?.IsAdmin ?? false;
        }
        catch
        {
            IsAdminUser = false;
        }
    }

    protected async Task LoadAssignedUserIdsAsync()
    {
        UsersErrorText = null;
        await SetUsersBusyAsync();

        try
        {
            var userIds = await ApiClient.GetBidUsersAsync(BidId);

            AssignedUsers.Clear();

            if (userIds is not null)
            {
                foreach (var userId in userIds)
                {
                    if (string.IsNullOrWhiteSpace(userId))
                        continue;

                    var user = AllUsers.FirstOrDefault(u => u.Id == userId);
                    if (user is not null)
                    {
                        AssignedUsers.Add(new UserOption(user.Id, user.Name));
                        continue;
                    }

                    // Non-admin users cannot query the full user directory, so fall back to ids.
                    AssignedUsers.Add(new UserOption(userId, userId));
                }
            }
        }
        catch (Exception ex)
        {
            UsersErrorText = ex.ToString();
        }
        finally
        {
            IsUsersBusy = false;
        }
    }

    protected async Task LoadUsersAsync()
    {
        if (!CanManageBidUsers)
            return;

        UsersErrorText = null;
        await SetUsersBusyAsync();

        try
        {
            var users = await ApiClient.GetUsersAsync();

            AvailableUsers.Clear();
            AllUsers.Clear();

            if (users is not null)
            {
                foreach (var user in users)
                {
                    if (string.IsNullOrWhiteSpace(user.Id) || string.IsNullOrWhiteSpace(user.Name))
                        continue;

                    AllUsers.Add(new UserOption(user.Id, user.Name));
                }
            }
        }
        catch (Exception ex)
        {
            UsersErrorText = ex.ToString();
        }
        finally
        {
            IsUsersBusy = false;
        }
    }

    protected void SyncAvailableUsers()
    {
        var assignedIds = new HashSet<string>(
            AssignedUsers.Select(user => user.Id),
            StringComparer.OrdinalIgnoreCase);

        AvailableUsers.Clear();
        foreach (var user in AllUsers)
        {
            if (assignedIds.Contains(user.Id))
                continue;

            AvailableUsers.Add(new UserOption(user.Id, user.Name));
        }
    }

    protected async Task LoadQuestionUsersAsync()
    {
        if (!CanManageQuestionUsers)
            return;

        if (Bid?.Questions is null || Bid.Questions.Count == 0)
            return;

        if (string.IsNullOrWhiteSpace(BidId))
            return;

        UsersErrorText = null;
        await SetUsersBusyAsync();

        var bidAssignedIds = new HashSet<string>(
            AssignedUsers.Select(user => user.Id),
            StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var question in Bid.Questions)
            {
                if (string.IsNullOrWhiteSpace(question.Id))
                    continue;

                var assignments = await ApiClient.GetQuestionUsersAsync(BidId, question.Id);

                question.QuestionAssignments.Clear();

                if (assignments is null)
                    continue;

                foreach (var assignment in assignments)
                {
                    if (string.IsNullOrWhiteSpace(assignment.UserId))
                        continue;

                    if (!bidAssignedIds.Contains(assignment.UserId))
                        continue;

                    question.QuestionAssignments.Add(new QuestionAssignmentResponse
                    {
                        UserId = assignment.UserId,
                        Role = assignment.Role
                    });
                }
            }
        }
        catch (Exception ex)
        {
            UsersErrorText = ex.ToString();
            _ = BannerState.ShowAsync("Could not load question users.");
        }
        finally
        {
            IsUsersBusy = false;
        }
    }

    protected async Task LoadQuestionFinalAnswersAsync()
    {
        if (Bid?.Questions is null || Bid.Questions.Count == 0)
            return;

        if (string.IsNullOrWhiteSpace(BidId))
            return;

        foreach (var question in Bid.Questions)
        {
            if (string.IsNullOrWhiteSpace(question.Id))
                continue;

            try
            {
                var result = await ApiClient.TryGetFinalAnswerAsync(BidId, question.Id);
                question.FinalAnswer = result?.AnswerText ?? string.Empty;
                question.ReadyForSubmission = result?.ReadyForSubmission ?? false;
                question.FinalAnswerComments = result?.Comments ?? new List<DraftCommentResponse>();
            }
            catch (Exception ex)
            {
                ErrorText = ex.ToString();
                question.FinalAnswer = string.Empty;
                question.ReadyForSubmission = false;
                question.FinalAnswerComments = new List<DraftCommentResponse>();
            }
        }
    }

    protected async Task LoadBidFilesAsync()
    {
        if (string.IsNullOrWhiteSpace(BidId))
            return;

        FilesErrorText = null;
        await SetFilesBusyAsync();

        try
        {
            var files = await ApiClient.GetBidFilesAsync(BidId);
            BidFiles.Clear();
            if (files is not null)
                BidFiles.AddRange(files.OrderByDescending(x => x.UploadedAtUtc));
        }
        catch (Exception ex)
        {
            FilesErrorText = ex.ToString();
            _ = BannerState.ShowAsync("Could not load bid files.");
        }
        finally
        {
            IsFilesBusy = false;
        }
    }

    protected async Task UploadBidFileAsync(InputFileChangeEventArgs e)
    {
        var file = e.File;
        if (file is null || string.IsNullOrWhiteSpace(BidId))
            return;

        FilesErrorText = null;
        await SetFilesBusyAsync();

        try
        {
            var uploaded = await ApiClient.UploadBidFileAsync(BidId, file);
            BidFiles.Insert(0, uploaded);
            _ = BannerState.ShowAsync($"Uploaded {uploaded.FileName}.", "alert-success");
        }
        catch (Exception ex)
        {
            FilesErrorText = ex.ToString();
            _ = BannerState.ShowAsync("Could not upload file.");
        }
        finally
        {
            IsFilesBusy = false;
        }
    }

    protected async Task DownloadBidFileAsync(BidFileResponse file)
    {
        if (file is null || string.IsNullOrWhiteSpace(BidId) || string.IsNullOrWhiteSpace(file.Id))
            return;

        FilesErrorText = null;
        await SetFilesBusyAsync();

        try
        {
            var downloaded = await ApiClient.DownloadBidFileAsync(BidId, file.Id);
            var base64 = Convert.ToBase64String(downloaded.Content);
            await JS.InvokeVoidAsync(
                "bidManage.downloadFileFromBase64",
                downloaded.FileName,
                downloaded.ContentType,
                base64);
        }
        catch (Exception ex)
        {
            FilesErrorText = ex.ToString();
            _ = BannerState.ShowAsync("Could not download file.");
        }
        finally
        {
            IsFilesBusy = false;
        }
    }

    protected async Task DeleteBidFileAsync(BidFileResponse file)
    {
        if (file is null || string.IsNullOrWhiteSpace(BidId) || string.IsNullOrWhiteSpace(file.Id))
            return;

        FilesErrorText = null;
        await SetFilesBusyAsync();

        try
        {
            await ApiClient.DeleteBidFileAsync(BidId, file.Id);
            BidFiles.RemoveAll(x => string.Equals(x.Id, file.Id, StringComparison.OrdinalIgnoreCase));
            _ = BannerState.ShowAsync($"Deleted {file.FileName}.", "alert-success");
        }
        catch (Exception ex)
        {
            FilesErrorText = ex.ToString();
            _ = BannerState.ShowAsync("Could not delete file.");
        }
        finally
        {
            IsFilesBusy = false;
        }
    }

    protected void SetLeftNav(LeftNavSection section)
    {
        LeftNav = section;

        if (section == LeftNavSection.Questions)
        {
            // Ensure a category is selected
            if (string.IsNullOrWhiteSpace(ActiveCategory))
            {
                var groups = CategoryGroups;
                if (groups.Count > 0)
                    SelectCategory(groups[0].Key);
            }
        }
    }

    protected void SelectCategory(string category)
    {
        ActiveCategory = category;

        var first = QuestionsInCategory.FirstOrDefault();
        if (first is not null)
            ActiveQuestionId = first.Id;

        ActiveInnerTab = CanManageQuestionUsers ? InnerTab.Users : InnerTab.Draft;
        QuestionErrorText = null;
    }

    protected void SelectQuestion(string? questionId)
    {
        ActiveQuestionId = questionId;
        PendingQuestionUserRoles.Clear();
        ActiveInnerTab = CanManageQuestionUsers ? InnerTab.Users : InnerTab.Draft;
        QuestionErrorText = null;
        DraftErrorText = null;
        ShowNewDraft = false;
        NewDraftText = null;
        ChatQuestionText = null;
    }

    protected Task GoToFinalAnswerFromSummaryAsync(string? questionId)
    {
        if (Bid?.Questions is null || string.IsNullOrWhiteSpace(questionId))
            return Task.CompletedTask;

        var question = Bid.Questions.FirstOrDefault(q =>
            string.Equals(q.Id, questionId, StringComparison.OrdinalIgnoreCase));
        if (question is null)
            return Task.CompletedTask;

        LeftNav = LeftNavSection.Questions;
        ActiveCategory = NormaliseCategory(question.Category);
        ActiveQuestionId = question.Id;
        PendingQuestionUserRoles.Clear();
        ActiveInnerTab = InnerTab.FinalAnswer;
        QuestionErrorText = null;
        DraftErrorText = null;
        ShowNewDraft = false;
        NewDraftText = null;
        ChatQuestionText = null;

        return Task.CompletedTask;
    }

    protected async Task UpdateBidStatusFromSummaryAsync(BidStatus status)
    {
        if (Bid is null || string.IsNullOrWhiteSpace(BidId))
            return;

        if (Bid.Status == status)
            return;

        await SetBusyAsync();
        try
        {
            await ApiClient.UpdateBidStatusAsync(BidId, status);
            Bid.Status = status;
            _ = BannerState.ShowAsync($"Bid status updated to {status}.", "alert-success");
        }
        catch (Exception ex)
        {
            ErrorText = ex.ToString();
            _ = BannerState.ShowAsync("Could not update bid status.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected async Task PushBidToLibraryFromSummaryAsync()
    {
        if (Bid is null || string.IsNullOrWhiteSpace(BidId))
            return;

        if (Bid.BidLibraryPush is not null)
            return;

        await SetBusyAsync();
        try
        {
            var result = await ApiClient.PushBidToLibraryAsync(BidId);
            Bid.BidLibraryPush = result;
            _ = BannerState.ShowAsync("Bid pushed to bid library.", "alert-success");
        }
        catch (Exception ex)
        {
            ErrorText = ex.ToString();
            _ = BannerState.ShowAsync("Could not push bid to bid library.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ReadDeepLinkTargetFromQuery()
    {
        var uri = Nav.ToAbsoluteUri(Nav.Uri);
        if (string.IsNullOrWhiteSpace(uri.Query))
            return;

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var sep = pair.IndexOf('=');
            var rawKey = sep < 0 ? pair : pair[..sep];
            var rawValue = sep < 0 ? string.Empty : pair[(sep + 1)..];
            var key = Uri.UnescapeDataString(rawKey);
            var value = Uri.UnescapeDataString(rawValue);

            if (key.Equals("questionId", StringComparison.OrdinalIgnoreCase))
                _targetQuestionId = value;
            else if (key.Equals("commentId", StringComparison.OrdinalIgnoreCase))
                _targetCommentId = value;
        }
    }

    private async Task ApplyDeepLinkTargetIfPresentAsync()
    {
        if (_targetApplied || Bid?.Questions is null || Bid.Questions.Count == 0)
            return;

        if (string.IsNullOrWhiteSpace(_targetQuestionId))
        {
            _targetApplied = true;
            return;
        }

        var question = Bid.Questions.FirstOrDefault(q =>
            string.Equals(q.Id, _targetQuestionId, StringComparison.OrdinalIgnoreCase));
        if (question is null)
        {
            _targetApplied = true;
            return;
        }

        LeftNav = LeftNavSection.Questions;
        ActiveCategory = NormaliseCategory(question.Category);
        ActiveQuestionId = question.Id;

        if (string.IsNullOrWhiteSpace(_targetCommentId))
        {
            _targetApplied = true;
            return;
        }

        await LoadQuestionDraftResponsesAsync();
        if (TrySelectDraftCommentTarget(question, _targetCommentId))
        {
            _targetApplied = true;
            return;
        }

        if (question.FinalAnswerComments.Any(c => string.Equals(c.Id, _targetCommentId, StringComparison.OrdinalIgnoreCase)))
        {
            ActiveInnerTab = InnerTab.FinalAnswer;
            _pendingScrollElementId = $"final-answer-comment-{_targetCommentId}";
            _targetApplied = true;
            return;
        }

        await LoadRedReviewAsync();
        if (question.RedReviewComments.Any(c => string.Equals(c.Id, _targetCommentId, StringComparison.OrdinalIgnoreCase)))
        {
            ActiveInnerTab = InnerTab.RedReview;
            _pendingScrollElementId = $"red-review-comment-{_targetCommentId}";
        }

        _targetApplied = true;
    }

    private bool TrySelectDraftCommentTarget(BidQuestionModel question, string commentId)
    {
        if (question.DraftResponses is null || question.DraftResponses.Count == 0)
            return false;

        foreach (var draft in question.DraftResponses)
        {
            if (draft.Comments is null || draft.Comments.Count == 0)
                continue;

            if (!draft.Comments.Any(c => string.Equals(c.Id, commentId, StringComparison.OrdinalIgnoreCase)))
                continue;

            ActiveInnerTab = InnerTab.Draft;
            _pendingScrollElementId = $"draft-comment-{commentId}";
            return true;
        }

        return false;
    }

    protected BidQuestionModel? ActiveQuestion
        => Bid?.Questions?.FirstOrDefault(q => q.Id == ActiveQuestionId);

    protected List<(string Key, int Count)> CategoryGroups
    {
        get
        {
            if (Bid?.Questions is null) return new();

            return Bid.Questions
                .GroupBy(q => NormaliseCategory(q.Category))
                .OrderBy(g => g.Key)
                .Select(g => (g.Key, g.Count()))
                .ToList();
        }
    }

    protected List<BidQuestionModel> QuestionsInCategory
    {
        get
        {
            if (Bid?.Questions is null) return new();

            var cat = string.IsNullOrWhiteSpace(ActiveCategory)
                ? "Uncategorised"
                : ActiveCategory;

            var list = Bid.Questions
                .Where(q => NormaliseCategory(q.Category) == cat)
                .OrderBy(q => q.QuestionOrderIndex <= 0 ? int.MaxValue : q.QuestionOrderIndex)
                .ThenBy(q => q.Number)
                .ToList();

            // Keep ActiveQuestionId valid
            if (list.Count > 0 && !list.Any(q => q.Id == ActiveQuestionId))
                ActiveQuestionId = list[0].Id;

            return list;
        }
    }

    protected IReadOnlyDictionary<string, string> AssignedUserNamesById
        => AssignedUsers
            .Where(user => !string.IsNullOrWhiteSpace(user.Id))
            .ToDictionary(user => user.Id, user => user.Name, StringComparer.OrdinalIgnoreCase);

    protected async Task ShowRedReviewTabAsync()
    {
        ActiveInnerTab = InnerTab.RedReview;
        await LoadRedReviewAsync();
    }

    protected int QuestionsWithoutFinalAnswerCount
        => Bid?.Questions?.Count(q => string.IsNullOrWhiteSpace(q.FinalAnswer)) ?? 0;

    protected int QuestionsWithoutUsersCount
        => Bid?.Questions?.Count(q => q.QuestionAssignments.Count == 0) ?? 0;

    protected static string NormaliseCategory(string? category)
        => string.IsNullOrWhiteSpace(category) ? "Uncategorised" : category;

    // ---- Actions (wire to API later) ----

    protected async Task SaveOverview()
    {
        if (Bid is null) return;

        await SetBusyAsync();
        try
        {
            // TODO: call API to save overview
            await Task.Delay(150);
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected async Task SaveQuestion()
    {
        if (Bid is null) return;
        if (ActiveQuestion is null) return;

        await SetBusyAsync();
        try
        {
            // TODO: call API to save question edits
            await Task.Delay(150);
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected async Task SubmitChatQuestionAsync(BidQuestionModel q)
    {
        if (string.IsNullOrWhiteSpace(BidId) || string.IsNullOrWhiteSpace(q.Id))
            return;

        await SetBusyAsync();
        QuestionErrorText = null;

        try
        {
            var url = $"api/ai/questions/{Uri.EscapeDataString(q.Id)}";
            var payload = new ChatQuestionRequest
            {
                BidId = Bid.Id,
                QuestionId = q.Id,
                FreeTextQuestion = ChatQuestionText ?? string.Empty,
                ThreadId = q.ChatThreadId
            };
            var response = await Http.PostAsJsonAsync(url, payload);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Failed to submit question: {(int)response.StatusCode} {response.ReasonPhrase}");

            var res = await response.Content.ReadFromJsonAsync<ChatQuestionResponse>();

            ActiveQuestion.ChatResponse = res?.Response ?? string.Empty;
            ActiveQuestion.ChatThreadId = res?.ThreadId;
        }
        catch (Exception ex)
        {
            QuestionErrorText = ex.ToString();
            _ = BannerState.ShowAsync("Could not submit question.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected async Task SaveFinalAnswer()
    {
        if (ActiveQuestion is null) return;

        await SetFinalAnswerAsync(ActiveQuestion.FinalAnswer ?? string.Empty, showSuccessBanner: true);
    }
    
    protected async Task SaveRedReview()
    {
        if (ActiveQuestion is null) return;
        await SetRedReviewAsync(showSuccessBanner: true);
    }

    protected async Task PromoteRedReviewToFinal()
    {
        if (ActiveQuestion is null)
            return;

        var promotedText = ActiveQuestion.RedReviewAnswer ?? string.Empty;
        var saved = await SetFinalAnswerAsync(promotedText, showSuccessBanner: false);
        if (!saved)
            return;

        ActiveQuestion.FinalAnswer = promotedText;
        ActiveInnerTab = InnerTab.FinalAnswer;
        _ = BannerState.ShowAsync("Red review promoted to final answer.", "alert-success");
    }

    protected async Task<bool> SetFinalAnswerAsync(string answerText, bool showSuccessBanner)
    {
        if (ActiveQuestion is null)
            return false;

        if (string.IsNullOrWhiteSpace(BidId) || string.IsNullOrWhiteSpace(ActiveQuestion.Id))
            return false;

        await SetBusyAsync();
        try
        {
            await ApiClient.SaveFinalAnswerAsync(
                BidId,
                ActiveQuestion.Id,
                answerText ?? string.Empty,
                ActiveQuestion.ReadyForSubmission);

            if (showSuccessBanner)
                _ = BannerState.ShowAsync("Final answer saved.", "alert-success");

            return true;
        }
        catch (Exception ex)
        {
            QuestionErrorText = ex.ToString();
            _ = BannerState.ShowAsync("Could not save final answer.");
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected async Task LoadRedReviewAsync(bool force = false)
    {
        if (ActiveQuestion is null)
            return;

        if (string.IsNullOrWhiteSpace(BidId) || string.IsNullOrWhiteSpace(ActiveQuestion.Id))
            return;

        if (ActiveQuestion.IsRedReviewLoaded && !force)
            return;

        await SetBusyAsync();
        try
        {
            var url = $"api/bids/{Uri.EscapeDataString(BidId)}/questions/{Uri.EscapeDataString(ActiveQuestion.Id)}/red-review";
            var response = await Http.GetAsync(url);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                ActiveQuestion.RedReviewAnswer ??= string.Empty;
                ActiveQuestion.RedReviewState = RedReviewState.Pending;
                ActiveQuestion.RedReviewReviewers.Clear();
                ActiveQuestion.RedReviewComments.Clear();
                EnsureRedReviewReviewerStateEntries(ActiveQuestion);
                ActiveQuestion.IsRedReviewLoaded = true;
                return;
            }

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Failed to load red review: {(int)response.StatusCode} {response.ReasonPhrase}");

            var review = await response.Content.ReadFromJsonAsync<RedReviewResponse>();
            if (review is null)
                throw new InvalidOperationException("Red review response was empty.");

            ActiveQuestion.RedReviewAnswer = review.ResultText;
            ActiveQuestion.RedReviewState = review.State;
            ActiveQuestion.RedReviewReviewers = review.Reviewers
                .Where(reviewer => !string.IsNullOrWhiteSpace(reviewer.UserId))
                .Select(reviewer => new RedReviewReviewerResponse
                {
                    UserId = reviewer.UserId,
                    State = reviewer.State
                })
                .GroupBy(reviewer => reviewer.UserId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            ActiveQuestion.RedReviewComments = review.Comments ?? new List<DraftCommentResponse>();

            EnsureRedReviewReviewerStateEntries(ActiveQuestion);
            ActiveQuestion.IsRedReviewLoaded = true;
        }
        catch (Exception ex)
        {
            QuestionErrorText = ex.ToString();
            _ = BannerState.ShowAsync("Could not load red review.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected async Task ShowDraftTabAsync()
    {
        ActiveInnerTab = InnerTab.Draft;
        QuestionErrorText = null;

        await LoadQuestionDraftResponsesAsync();
    }

    protected async Task LoadQuestionDraftResponsesAsync()
    {
        if (ActiveQuestion is null)
            return;

        if (string.IsNullOrWhiteSpace(BidId) || string.IsNullOrWhiteSpace(ActiveQuestion.Id))
            return;

        await SetDraftBusyAsync();
        DraftErrorText = null;

        try
        {
            var url = $"api/bids/{Uri.EscapeDataString(BidId)}/questions/{Uri.EscapeDataString(ActiveQuestion.Id)}/drafts";
            var responses = await Http.GetFromJsonAsync<List<DraftResponse>>(url) ?? new List<DraftResponse>();

            ActiveQuestion.DraftResponses = responses;
            DraftCommentTextsByDraftId.Clear();
        }
        catch (Exception ex)
        {
            DraftErrorText = ex.ToString();
            _ = BannerState.ShowAsync("Could not load draft responses.");
        }
        finally
        {
            IsDraftBusy = false;
        }
    }

    protected void ShowNewDraftEditor()
    {
        ShowNewDraft = true;
        NewDraftText = string.Empty;
    }

    protected void CancelNewDraft()
    {
        ShowNewDraft = false;
        NewDraftText = null;
    }

    protected async Task SubmitDraftResponseAsync()
    {
        if (ActiveQuestion is null)
            return;

        if (string.IsNullOrWhiteSpace(BidId) || string.IsNullOrWhiteSpace(ActiveQuestion.Id))
            return;

        var text = (NewDraftText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            _ = BannerState.ShowAsync("Draft response cannot be empty.");
            return;
        }

        await SetDraftBusyAsync();
        DraftErrorText = null;

        try
        {
            var url = $"api/bids/{Uri.EscapeDataString(BidId)}/questions/{Uri.EscapeDataString(ActiveQuestion.Id)}/drafts";
            var request = new DraftRequest
            {
                Response = text
            };
            var response = await Http.PostAsJsonAsync(url, request);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Failed to add draft response: {(int)response.StatusCode} {response.ReasonPhrase}");

            var resp = await response.Content.ReadFromJsonAsync<CreateAssetResponse>();
                
            ActiveQuestion.DraftResponses.Add(new DraftResponse
            {
                Id = resp.Id,
                Response = text
            });

            ShowNewDraft = false;
            NewDraftText = null;
        }
        catch (Exception ex)
        {
            DraftErrorText = ex.ToString();
            _ = BannerState.ShowAsync("Could not add draft response.");
        }
        finally
        {
            IsDraftBusy = false;
        }
    }

    protected async Task AddChatResponseToDraftsAsync(BidQuestionModel q)
    {
        if (string.IsNullOrWhiteSpace(BidId) || string.IsNullOrWhiteSpace(q.Id))
            return;

        var text = (q.ChatResponse ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            _ = BannerState.ShowAsync("Chat response is empty.");
            return;
        }

        await SetDraftBusyAsync();
        DraftErrorText = null;

        try
        {
            var url = $"api/bids/{Uri.EscapeDataString(BidId)}/questions/{Uri.EscapeDataString(q.Id)}/drafts";
            var request = new DraftRequest
            {
                Response = text
            };
            var response = await Http.PostAsJsonAsync(url, request);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Failed to add draft response: {(int)response.StatusCode} {response.ReasonPhrase}");

            await LoadQuestionDraftResponsesAsync();
        }
        catch (Exception ex)
        {
            DraftErrorText = ex.ToString();
            _ = BannerState.ShowAsync("Could not add chat response to drafts.");
        }
        finally
        {
            IsDraftBusy = false;
        }
    }

    protected async Task UpdateDraftResponseAsync(string bidId, string questionId, string draftId, string draft)
    {
        if (string.IsNullOrWhiteSpace(bidId) || string.IsNullOrWhiteSpace(questionId))
            return;

        if (string.IsNullOrWhiteSpace(draftId))
        {
            _ = BannerState.ShowAsync("Draft response id is missing.");
            return;
        }

        var text = (draft ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            _ = BannerState.ShowAsync("Draft response cannot be empty.");
            return;
        }

        await SetDraftBusyAsync();
        DraftErrorText = null;

        try
        {
            var url =
                $"api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/drafts/{Uri.EscapeDataString(draftId)}";
            var request = new UpdateDraftRequest
            {
                Id = draftId,
                Response = text
            };
            var httpResponse = await Http.PutAsJsonAsync(url, request);

            if (!httpResponse.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Failed to update draft response: {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}");
        }
        catch (Exception ex)
        {
            DraftErrorText = ex.ToString();
            _ = BannerState.ShowAsync("Could not update draft response.");
        }
        finally
        {
            IsDraftBusy = false;
        }
    }

    protected async Task DeleteDraftResponseAsync(string bidId, string questionId, string draftId)
    {
        if (string.IsNullOrWhiteSpace(bidId) || string.IsNullOrWhiteSpace(questionId))
            return;

        if (string.IsNullOrWhiteSpace(draftId))
        {
            _ = BannerState.ShowAsync("Draft response id is missing.");
            return;
        }

        await SetDraftBusyAsync();
        DraftErrorText = null;

        try
        {
            var url =
                $"api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/drafts?responseId={Uri.EscapeDataString(draftId)}";
            var httpResponse = await Http.DeleteAsync(url);

            if (!httpResponse.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Failed to delete draft response: {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}");

            var activeQuestion = ActiveQuestion;
            if (activeQuestion is not null)
            {
                activeQuestion.DraftResponses.RemoveAll(d => d.Id == draftId);
                DraftCommentTextsByDraftId.Remove(draftId);
            }
        }
        catch (Exception ex)
        {
            DraftErrorText = ex.ToString();
            _ = BannerState.ShowAsync("Could not delete draft response.");
        }
        finally
        {
            IsDraftBusy = false;
        }
    }

    protected async Task PromoteDraftToReview(string? draftText)
    {
        if (ActiveQuestion is null)
            return;

        ActiveQuestion.RedReviewAnswer = draftText ?? string.Empty;
        var saved = await SetRedReviewAsync(showSuccessBanner: false);
        if (!saved)
            return;

        ActiveInnerTab = InnerTab.RedReview;
        EnsureRedReviewReviewerStateEntries(ActiveQuestion);
        ActiveQuestion.IsRedReviewLoaded = true;
        _ = BannerState.ShowAsync("Draft promoted to review.", "alert-success");
    }

    protected async Task<bool> SetRedReviewAsync(bool showSuccessBanner)
    {
        if (ActiveQuestion is null)
            return false;

        if (string.IsNullOrWhiteSpace(BidId) || string.IsNullOrWhiteSpace(ActiveQuestion.Id))
            return false;

        await SetBusyAsync();
        try
        {
            EnsureRedReviewReviewerStateEntries(ActiveQuestion);

            var url = $"api/bids/{Uri.EscapeDataString(BidId)}/questions/{Uri.EscapeDataString(ActiveQuestion.Id)}/red-review";
            var payload = new UpdateRedReviewRequest
            {
                ResultText = ActiveQuestion.RedReviewAnswer ?? string.Empty,
                State = ActiveQuestion.RedReviewState,
                Reviewers = ActiveQuestion.RedReviewReviewers
                    .Select(reviewer => new RedReviewReviewerResponse
                    {
                        UserId = reviewer.UserId,
                        State = reviewer.State
                    })
                    .ToList()
            };

            var response = await Http.PutAsJsonAsync(url, payload);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Failed to save red review: {(int)response.StatusCode} {response.ReasonPhrase}");

            ActiveQuestion.IsRedReviewLoaded = true;
            if (showSuccessBanner)
                _ = BannerState.ShowAsync("Red review saved.", "alert-success");

            return true;
        }
        catch (Exception ex)
        {
            QuestionErrorText = ex.ToString();
            _ = BannerState.ShowAsync("Could not save red review.");
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected async Task AddUserAsync(UserOption user)
    {
        if (!CanManageBidUsers)
            return;

        if (IsUsersBusy)
            return;

        if (AssignedUsers.Any(u => u.Id == user.Id))
            return;

        if (string.IsNullOrWhiteSpace(BidId))
        {
            _ = BannerState.ShowAsync("Could not add user to this bid.");
            return;
        }

        UsersErrorText = null;
        await SetUsersBusyAsync();
        try
        {
            var url = $"api/bids/{Uri.EscapeDataString(BidId)}/users";
            var payload = new UserAssignmentRequest { UserId = user.Id };
            var response = await Http.PostAsJsonAsync(url, payload);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Failed to add user: {(int)response.StatusCode} {response.ReasonPhrase}");

            AvailableUsers.Remove(user);
            AssignedUsers.Add(user);
        }
        catch (Exception ex)
        {
            UsersErrorText = ex.ToString();
            _ = BannerState.ShowAsync("Could not add user to this bid.");
        }
        finally
        {
            IsUsersBusy = false;
        }
    }

    protected async Task RemoveUserAsync(UserOption user)
    {
        if (!CanManageBidUsers)
            return;

        if (IsUsersBusy)
            return;

        if (IsUserAssignedToQuestion(user.Id))
        {
            _ = BannerState.ShowAsync("Cannot remove user: assigned to a question.");
            return;
        }

        if (string.IsNullOrWhiteSpace(BidId))
        {
            _ = BannerState.ShowAsync("Could not remove user from this bid.");
            return;
        }

        UsersErrorText = null;
        await SetUsersBusyAsync();
        try
        {
            var url = $"api/bids/{Uri.EscapeDataString(BidId)}/users";
            var payload = JsonContent.Create(new UserAssignmentRequest { UserId = user.Id });
            using var request = new HttpRequestMessage(HttpMethod.Delete, url)
            {
                Content = payload
            };
            var response = await Http.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Failed to remove user: {(int)response.StatusCode} {response.ReasonPhrase}");

            AssignedUsers.Remove(user);
            AvailableUsers.Add(user);
        }
        catch (Exception ex)
        {
            UsersErrorText = ex.ToString();
            _ = BannerState.ShowAsync("Could not remove user from this bid.");
        }
        finally
        {
            IsUsersBusy = false;
        }
    }

    protected bool IsUserAssignedToQuestion(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return false;

        return Bid?.Questions?.Any(q =>
            q.QuestionAssignments.Any(a => string.Equals(a.UserId, userId, StringComparison.OrdinalIgnoreCase))) ?? false;
    }

    protected QuestionUserRole GetPendingQuestionUserRole(string userId)
    {
        if (PendingQuestionUserRoles.TryGetValue(userId, out var role))
            return role;

        return QuestionUserRole.Reviewer;
    }

    protected void SetPendingQuestionUserRole(string userId, ChangeEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        if (TryParseQuestionUserRole(args?.Value, out var role))
            PendingQuestionUserRoles[userId] = role;
    }

    protected Task OnPendingQuestionUserRoleChangedAsync((string UserId, ChangeEventArgs Args) change)
    {
        SetPendingQuestionUserRole(change.UserId, change.Args);
        return Task.CompletedTask;
    }

    protected Task OnAddQuestionUserRequestedAsync((string UserId, QuestionUserRole Role) request)
        => AddQuestionUserAsync(request.UserId, request.Role);

    protected async Task AddQuestionUserAsync(string userId, QuestionUserRole role)
    {
        if (!CanManageQuestionUsers)
            return;

        if (ActiveQuestion is null)
            return;

        if (string.IsNullOrWhiteSpace(userId))
            return;

        if (ActiveQuestion.QuestionAssignments.Any(a => string.Equals(a.UserId, userId, StringComparison.OrdinalIgnoreCase)))
            return;

        if (string.IsNullOrWhiteSpace(BidId) || string.IsNullOrWhiteSpace(ActiveQuestion.Id))
        {
            _ = BannerState.ShowAsync("Could not assign user to question.");
            return;
        }

        await SetUsersBusyAsync();
        try
        {
            var url = $"api/bids/{Uri.EscapeDataString(BidId)}/questions/{Uri.EscapeDataString(ActiveQuestion.Id)}/users";
            var payload = new QuestionUserAssignmentRequest { UserId = userId, Role = role };
            var response = await Http.PostAsJsonAsync(url, payload);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Failed to add question user: {(int)response.StatusCode} {response.ReasonPhrase}");

            if (role == QuestionUserRole.Owner)
            {
                foreach (var existing in ActiveQuestion.QuestionAssignments.Where(a => a.Role == QuestionUserRole.Owner))
                    existing.Role = QuestionUserRole.Reviewer;
            }

            ActiveQuestion.QuestionAssignments.Add(new QuestionAssignmentResponse
            {
                UserId = userId,
                Role = role
            });
            EnsureRedReviewReviewerStateEntries(ActiveQuestion);
            PendingQuestionUserRoles.Remove(userId);
        }
        catch (Exception ex)
        {
            UsersErrorText = ex.ToString();
            _ = BannerState.ShowAsync("Could not assign user to question.");
        }
        finally
        {
            IsUsersBusy = false;
        }
    }

    protected async Task RemoveQuestionUserAsync(string userId)
    {
        if (!CanManageQuestionUsers)
            return;

        if (ActiveQuestion is null)
            return;

        if (string.IsNullOrWhiteSpace(userId))
            return;

        if (!ActiveQuestion.QuestionAssignments.Any(a => string.Equals(a.UserId, userId, StringComparison.OrdinalIgnoreCase)))
            return;

        if (string.IsNullOrWhiteSpace(BidId) || string.IsNullOrWhiteSpace(ActiveQuestion.Id))
        {
            _ = BannerState.ShowAsync("Could not remove user from question.");
            return;
        }

        await SetUsersBusyAsync();
        try
        {
            var url = $"api/bids/{Uri.EscapeDataString(BidId)}/questions/{Uri.EscapeDataString(ActiveQuestion.Id)}/users";
            var payload = JsonContent.Create(new UserAssignmentRequest { UserId = userId });
            using var request = new HttpRequestMessage(HttpMethod.Delete, url)
            {
                Content = payload
            };
            var response = await Http.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Failed to remove question user: {(int)response.StatusCode} {response.ReasonPhrase}");

            ActiveQuestion.QuestionAssignments.RemoveAll(a => string.Equals(a.UserId, userId, StringComparison.OrdinalIgnoreCase));
            EnsureRedReviewReviewerStateEntries(ActiveQuestion);
        }
        catch (Exception ex)
        {
            UsersErrorText = ex.ToString();
            _ = BannerState.ShowAsync("Could not remove user from question.");
        }
        finally
        {
            IsUsersBusy = false;
        }
    }

    protected async Task UpdateQuestionUserRoleAsync(string userId, QuestionUserRole role)
    {
        if (!CanManageQuestionUsers)
            return;

        if (ActiveQuestion is null)
            return;

        if (string.IsNullOrWhiteSpace(userId))
            return;

        var assignment = ActiveQuestion.QuestionAssignments
            .FirstOrDefault(a => string.Equals(a.UserId, userId, StringComparison.OrdinalIgnoreCase));

        if (assignment is null || assignment.Role == role)
            return;

        if (string.IsNullOrWhiteSpace(BidId) || string.IsNullOrWhiteSpace(ActiveQuestion.Id))
        {
            _ = BannerState.ShowAsync("Could not update user role for question.");
            return;
        }

        await SetUsersBusyAsync();
        try
        {
            var url = $"api/bids/{Uri.EscapeDataString(BidId)}/questions/{Uri.EscapeDataString(ActiveQuestion.Id)}/users";
            var payload = new QuestionUserAssignmentRequest { UserId = userId, Role = role };
            var response = await Http.PutAsJsonAsync(url, payload);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Failed to update question user role: {(int)response.StatusCode} {response.ReasonPhrase}");

            if (role == QuestionUserRole.Owner)
            {
                foreach (var existing in ActiveQuestion.QuestionAssignments.Where(a => a.Role == QuestionUserRole.Owner))
                    existing.Role = QuestionUserRole.Reviewer;
            }

            assignment.Role = role;
            EnsureRedReviewReviewerStateEntries(ActiveQuestion);
        }
        catch (Exception ex)
        {
            UsersErrorText = ex.ToString();
            _ = BannerState.ShowAsync("Could not update user role for question.");
        }
        finally
        {
            IsUsersBusy = false;
        }
    }

    protected async Task OnQuestionAssignedUserRoleChangedAsync(string userId, ChangeEventArgs args)
    {
        if (!TryParseQuestionUserRole(args?.Value, out var role))
            return;

        await UpdateQuestionUserRoleAsync(userId, role);
    }

    protected Task OnAssignedQuestionRoleChangedAsync((string UserId, ChangeEventArgs Args) change)
        => OnQuestionAssignedUserRoleChangedAsync(change.UserId, change.Args);

    protected Task OnNewDraftTextChangedAsync(string? text)
    {
        NewDraftText = text;
        return Task.CompletedTask;
    }

    protected Task OnUpdateDraftRequestedAsync((string QuestionId, string DraftId, string DraftText) update)
        => UpdateDraftResponseAsync(BidId, update.QuestionId, update.DraftId, update.DraftText);

    protected Task OnDeleteDraftRequestedAsync((string QuestionId, string DraftId) request)
        => DeleteDraftResponseAsync(BidId, request.QuestionId, request.DraftId);

    protected Task OnDraftCommentTextChangedAsync((string DraftId, string? Text) change)
    {
        if (string.IsNullOrWhiteSpace(change.DraftId))
            return Task.CompletedTask;

        DraftCommentTextsByDraftId[change.DraftId] = change.Text;
        return Task.CompletedTask;
    }

    protected Task OnSubmitDraftCommentRequestedAsync((string QuestionId, string DraftId, int? StartIndex, int? EndIndex, string SelectedText, List<string> MentionedUserIds) request)
        => SubmitDraftCommentAsync(BidId, request.QuestionId, request.DraftId, request.StartIndex, request.EndIndex, request.SelectedText, request.MentionedUserIds);

    protected Task OnDraftCommentCompletionChangedAsync((string QuestionId, string DraftId, string CommentId, bool IsComplete) request)
        => SetDraftCommentCompletionAsync(BidId, request.QuestionId, request.DraftId, request.CommentId, request.IsComplete);

    protected async Task SubmitDraftCommentAsync(
        string bidId,
        string questionId,
        string draftId,
        int? startIndex,
        int? endIndex,
        string selectedText,
        List<string> mentionedUserIds)
    {
        if (ActiveQuestion is null)
            return;

        if (string.IsNullOrWhiteSpace(bidId) || string.IsNullOrWhiteSpace(questionId) || string.IsNullOrWhiteSpace(draftId))
            return;

        if (!DraftCommentTextsByDraftId.TryGetValue(draftId, out var commentText))
            commentText = string.Empty;

        var text = (commentText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            _ = BannerState.ShowAsync("Comment cannot be empty.");
            return;
        }

        await SetDraftBusyAsync();
        DraftErrorText = null;

        try
        {
            var url =
                $"api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/drafts/{Uri.EscapeDataString(draftId)}/comments";
            var request = new AddDraftCommentRequest
            {
                Comment = text,
                UserId = "current-user",
                AuthorName = "Current user",
                MentionedUserIds = mentionedUserIds ?? new List<string>(),
                StartIndex = startIndex,
                EndIndex = endIndex,
                SelectedText = selectedText ?? string.Empty
            };

            var httpResponse = await Http.PostAsJsonAsync(url, request);
            if (!httpResponse.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Failed to add draft comment: {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}");

            var addedComment = await httpResponse.Content.ReadFromJsonAsync<DraftCommentResponse>();
            if (addedComment is null)
                throw new InvalidOperationException("Draft comment response was empty.");

            var activeDraft = ActiveQuestion.DraftResponses
                .FirstOrDefault(d => string.Equals(d.Id, draftId, StringComparison.OrdinalIgnoreCase));

            if (activeDraft is not null)
                activeDraft.Comments.Add(addedComment);

            DraftCommentTextsByDraftId[draftId] = string.Empty;
        }
        catch (Exception ex)
        {
            DraftErrorText = ex.ToString();
            _ = BannerState.ShowAsync("Could not add draft comment.");
        }
        finally
        {
            IsDraftBusy = false;
        }
    }

    protected async Task SetDraftCommentCompletionAsync(
        string bidId,
        string questionId,
        string draftId,
        string commentId,
        bool isComplete)
    {
        if (ActiveQuestion is null)
            return;

        if (string.IsNullOrWhiteSpace(bidId)
            || string.IsNullOrWhiteSpace(questionId)
            || string.IsNullOrWhiteSpace(draftId)
            || string.IsNullOrWhiteSpace(commentId))
            return;

        await SetDraftBusyAsync();
        DraftErrorText = null;

        try
        {
            var updated = await ApiClient.SetDraftCommentCompletionAsync(
                bidId,
                questionId,
                draftId,
                commentId,
                isComplete);

            var activeDraft = ActiveQuestion.DraftResponses
                .FirstOrDefault(d => string.Equals(d.Id, draftId, StringComparison.OrdinalIgnoreCase));
            var existing = activeDraft?.Comments
                .FirstOrDefault(c => string.Equals(c.Id, commentId, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
                return;

            existing.IsComplete = updated.IsComplete;
        }
        catch (Exception ex)
        {
            DraftErrorText = ex.ToString();
            _ = BannerState.ShowAsync("Could not update draft comment.");
        }
        finally
        {
            IsDraftBusy = false;
        }
    }

    protected Task OnChatQuestionTextChangedAsync(string? text)
    {
        ChatQuestionText = text;
        return Task.CompletedTask;
    }

    protected Task SubmitChatForActiveQuestionAsync()
    {
        if (ActiveQuestion is null)
            return Task.CompletedTask;

        return SubmitChatQuestionAsync(ActiveQuestion);
    }

    protected async Task AddRedReviewInlineCommentAsync((string Comment, int? StartIndex, int? EndIndex, string SelectedText, List<string> MentionedUserIds) request)
    {
        if (ActiveQuestion is null)
            return;

        if (string.IsNullOrWhiteSpace(BidId) || string.IsNullOrWhiteSpace(ActiveQuestion.Id))
            return;

        var text = request.Comment?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            _ = BannerState.ShowAsync("Comment cannot be empty.");
            return;
        }

        await SetBusyAsync();
        try
        {
            var url = $"api/bids/{Uri.EscapeDataString(BidId)}/questions/{Uri.EscapeDataString(ActiveQuestion.Id)}/red-review/comments";
            var payload = new AddDraftCommentRequest
            {
                Comment = text,
                UserId = "current-user",
                AuthorName = "Current user",
                MentionedUserIds = request.MentionedUserIds ?? new List<string>(),
                StartIndex = request.StartIndex,
                EndIndex = request.EndIndex,
                SelectedText = request.SelectedText ?? string.Empty
            };

            var response = await Http.PostAsJsonAsync(url, payload);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Failed to add red review comment: {(int)response.StatusCode} {response.ReasonPhrase}");

            var added = await response.Content.ReadFromJsonAsync<DraftCommentResponse>();
            if (added is null)
                throw new InvalidOperationException("Red review comment response was empty.");

            ActiveQuestion.RedReviewComments.Add(added);
        }
        catch (Exception ex)
        {
            QuestionErrorText = ex.ToString();
            _ = BannerState.ShowAsync("Could not add red review comment.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected Task OnRedReviewCommentCompletionChangedAsync((string QuestionId, string CommentId, bool IsComplete) request)
        => SetRedReviewCommentCompletionAsync(BidId, request.QuestionId, request.CommentId, request.IsComplete);

    protected async Task SetRedReviewCommentCompletionAsync(string bidId, string questionId, string commentId, bool isComplete)
    {
        if (ActiveQuestion is null)
            return;

        if (string.IsNullOrWhiteSpace(bidId)
            || string.IsNullOrWhiteSpace(questionId)
            || string.IsNullOrWhiteSpace(commentId))
            return;

        await SetBusyAsync();
        try
        {
            var updated = await ApiClient.SetRedReviewCommentCompletionAsync(
                bidId,
                questionId,
                commentId,
                isComplete);

            var existing = ActiveQuestion.RedReviewComments
                .FirstOrDefault(c => string.Equals(c.Id, commentId, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
                return;

            existing.IsComplete = updated.IsComplete;
        }
        catch (Exception ex)
        {
            QuestionErrorText = ex.ToString();
            _ = BannerState.ShowAsync("Could not update red review comment.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected async Task AddFinalAnswerInlineCommentAsync((string Comment, int? StartIndex, int? EndIndex, string SelectedText, List<string> MentionedUserIds) request)
    {
        if (ActiveQuestion is null)
            return;

        if (string.IsNullOrWhiteSpace(BidId) || string.IsNullOrWhiteSpace(ActiveQuestion.Id))
            return;

        var text = request.Comment?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            _ = BannerState.ShowAsync("Comment cannot be empty.");
            return;
        }

        await SetBusyAsync();
        try
        {
            var url = $"api/bids/{Uri.EscapeDataString(BidId)}/questions/{Uri.EscapeDataString(ActiveQuestion.Id)}/final-answer/comments";
            var payload = new AddDraftCommentRequest
            {
                Comment = text,
                UserId = "current-user",
                AuthorName = "Current user",
                MentionedUserIds = request.MentionedUserIds ?? new List<string>(),
                StartIndex = request.StartIndex,
                EndIndex = request.EndIndex,
                SelectedText = request.SelectedText ?? string.Empty
            };

            var response = await Http.PostAsJsonAsync(url, payload);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Failed to add final answer comment: {(int)response.StatusCode} {response.ReasonPhrase}");

            var added = await response.Content.ReadFromJsonAsync<DraftCommentResponse>();
            if (added is null)
                throw new InvalidOperationException("Final answer comment response was empty.");

            ActiveQuestion.FinalAnswerComments.Add(added);
        }
        catch (Exception ex)
        {
            QuestionErrorText = ex.ToString();
            _ = BannerState.ShowAsync("Could not add final answer comment.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected Task OnFinalAnswerCommentCompletionChangedAsync((string QuestionId, string CommentId, bool IsComplete) request)
        => SetFinalAnswerCommentCompletionAsync(BidId, request.QuestionId, request.CommentId, request.IsComplete);

    protected async Task SetFinalAnswerCommentCompletionAsync(string bidId, string questionId, string commentId, bool isComplete)
    {
        if (ActiveQuestion is null)
            return;

        if (string.IsNullOrWhiteSpace(bidId)
            || string.IsNullOrWhiteSpace(questionId)
            || string.IsNullOrWhiteSpace(commentId))
            return;

        await SetBusyAsync();
        try
        {
            var updated = await ApiClient.SetFinalAnswerCommentCompletionAsync(
                bidId,
                questionId,
                commentId,
                isComplete);

            var existing = ActiveQuestion.FinalAnswerComments
                .FirstOrDefault(c => string.Equals(c.Id, commentId, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
                return;

            existing.IsComplete = updated.IsComplete;
        }
        catch (Exception ex)
        {
            QuestionErrorText = ex.ToString();
            _ = BannerState.ShowAsync("Could not update final answer comment.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected Task AddActiveChatResponseToDraftsAsync()
    {
        if (ActiveQuestion is null)
            return Task.CompletedTask;

        return AddChatResponseToDraftsAsync(ActiveQuestion);
    }

    protected static bool TryParseQuestionUserRole(object? value, out QuestionUserRole role)
    {
        var text = value?.ToString();
        return Enum.TryParse(text, true, out role);
    }

    protected List<RedReviewReviewerOption> GetRedReviewReviewers(BidQuestionModel question)
    {
        EnsureRedReviewReviewerStateEntries(question);

        var namesById = AssignedUsers
            .Where(user => !string.IsNullOrWhiteSpace(user.Id))
            .ToDictionary(user => user.Id, user => user.Name, StringComparer.OrdinalIgnoreCase);

        return question.RedReviewReviewers
            .Select(reviewer => new RedReviewReviewerOption(
                reviewer.UserId,
                namesById.TryGetValue(reviewer.UserId, out var name) && !string.IsNullOrWhiteSpace(name)
                    ? name
                    : reviewer.UserId,
                reviewer.State))
            .OrderBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    protected void EnsureRedReviewReviewerStateEntries(BidQuestionModel question)
    {
        var reviewerIds = question.QuestionAssignments
            .Where(assignment => assignment.Role == QuestionUserRole.Reviewer && !string.IsNullOrWhiteSpace(assignment.UserId))
            .Select(assignment => assignment.UserId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existingStates = question.RedReviewReviewers
            .Where(reviewer => !string.IsNullOrWhiteSpace(reviewer.UserId))
            .GroupBy(reviewer => reviewer.UserId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().State, StringComparer.OrdinalIgnoreCase);

        question.RedReviewReviewers = reviewerIds
            .Select(id => new RedReviewReviewerResponse
            {
                UserId = id,
                State = existingStates.TryGetValue(id, out var state) ? state : RedReviewState.Pending
            })
            .ToList();
    }

    protected List<UserOption> QuestionAvailableUsers
    {
        get
        {
            if (ActiveQuestion is null)
                return new List<UserOption>();

            var selectedIds = new HashSet<string>(
                ActiveQuestion.QuestionAssignments.Select(a => a.UserId),
                StringComparer.OrdinalIgnoreCase);

            return AssignedUsers
                .Where(user => !selectedIds.Contains(user.Id))
                .Select(user => new UserOption(user.Id, user.Name))
                .ToList();
        }
    }

    protected List<QuestionAssignedUserOption> QuestionAssignedUsers
    {
        get
        {
            if (ActiveQuestion is null)
                return new List<QuestionAssignedUserOption>();

            var namesById = AssignedUsers.ToDictionary(x => x.Id, x => x.Name, StringComparer.OrdinalIgnoreCase);

            return ActiveQuestion.QuestionAssignments
                .Select(assignment =>
                {
                    namesById.TryGetValue(assignment.UserId, out var name);
                    return new QuestionAssignedUserOption(
                        assignment.UserId,
                        name ?? assignment.UserId,
                        assignment.Role);
                })
                .ToList();
        }
    }
    
    // ---- Types (replace with your real DTOs) ----

    protected enum LeftNavSection { Overview, AllQuestions, Questions, Files, Users }
    protected enum InnerTab { Users, Draft, Chat, RedReview, FinalAnswer }

    protected static string FormatFileSize(long sizeBytes)
    {
        if (sizeBytes < 1024)
            return $"{sizeBytes} B";

        var kb = sizeBytes / 1024d;
        if (kb < 1024)
            return $"{kb:0.#} KB";

        var mb = kb / 1024d;
        if (mb < 1024)
            return $"{mb:0.#} MB";

        var gb = mb / 1024d;
        return $"{gb:0.#} GB";
    }
}
