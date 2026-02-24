using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.Forms;
using TalentSuite.Shared.Bids;
using TalentSuite.Shared.Users;

namespace TalentSuite.FrontEnd.Pages.Bids.Management;

public sealed class BidManageApiClient(HttpClient http)
{
    public sealed class DownloadedBidFile
    {
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = "application/octet-stream";
        public byte[] Content { get; set; } = Array.Empty<byte>();
    }

    public Task<Models.BidManageModel?> GetBidAsync(string bidId, CancellationToken ct = default)
        => http.GetFromJsonAsync<Models.BidManageModel>($"api/bids/{Uri.EscapeDataString(bidId)}", ct);

    public Task<List<BidFileResponse>?> GetBidFilesAsync(string bidId, CancellationToken ct = default)
        => http.GetFromJsonAsync<List<BidFileResponse>>($"api/bids/{Uri.EscapeDataString(bidId)}/files", ct);

    public async Task<BidFileResponse> UploadBidFileAsync(string bidId, IBrowserFile file, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        await using var stream = file.OpenReadStream(maxAllowedSize: 25_000_000, cancellationToken: ct);
        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType);
        form.Add(fileContent, "file", file.Name);

        var response = await http.PostAsync($"api/bids/{Uri.EscapeDataString(bidId)}/files", form, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Failed to upload file: {(int)response.StatusCode} {response.ReasonPhrase}");

        return await response.Content.ReadFromJsonAsync<BidFileResponse>(ct)
               ?? throw new InvalidOperationException("Upload response was empty.");
    }

    public async Task<DownloadedBidFile> DownloadBidFileAsync(string bidId, string fileId, CancellationToken ct = default)
    {
        var response = await http.GetAsync(
            $"api/bids/{Uri.EscapeDataString(bidId)}/files/{Uri.EscapeDataString(fileId)}",
            ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Failed to download file: {(int)response.StatusCode} {response.ReasonPhrase}");

        var content = await response.Content.ReadAsByteArrayAsync(ct);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                       ?? response.Content.Headers.ContentDisposition?.FileName
                       ?? "download.bin";

        return new DownloadedBidFile
        {
            FileName = fileName.Trim('"'),
            ContentType = contentType,
            Content = content
        };
    }

    public async Task DeleteBidFileAsync(string bidId, string fileId, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync(
            $"api/bids/{Uri.EscapeDataString(bidId)}/files/{Uri.EscapeDataString(fileId)}",
            ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException("File was not found.");

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Failed to delete file: {(int)response.StatusCode} {response.ReasonPhrase}");
    }

    public async Task UpdateBidStatusAsync(string bidId, BidStatus status, CancellationToken ct = default)
    {
        var response = await http.PatchAsJsonAsync(
            $"api/bids/{Uri.EscapeDataString(bidId)}/status",
            new UpdateBidStatusRequest { Status = status },
            ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Failed to update bid status: {(int)response.StatusCode} {response.ReasonPhrase}");
    }

    public async Task<BidLibraryPushResponse> PushBidToLibraryAsync(string bidId, CancellationToken ct = default)
    {
        var response = await http.PostAsync($"api/bids/{Uri.EscapeDataString(bidId)}/library-push", null, ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Failed to push bid to library: {(int)response.StatusCode} {response.ReasonPhrase}");

        return await response.Content.ReadFromJsonAsync<BidLibraryPushResponse>(ct)
               ?? throw new InvalidOperationException("Bid library push response was empty.");
    }

    public Task<List<UserResponse>?> GetUsersAsync(CancellationToken ct = default)
        => http.GetFromJsonAsync<List<UserResponse>>("api/users", ct);

    public Task<CurrentUserAuthorisationResponse?> GetMyAuthorisationAsync(CancellationToken ct = default)
        => http.GetFromJsonAsync<CurrentUserAuthorisationResponse>("api/users/me-authorisation", ct);

    public Task<List<string>?> GetBidUsersAsync(string bidId, CancellationToken ct = default)
        => http.GetFromJsonAsync<List<string>>($"api/bids/{Uri.EscapeDataString(bidId)}/users", ct);

    public Task<List<QuestionAssignmentResponse>?> GetQuestionUsersAsync(string bidId, string questionId, CancellationToken ct = default)
        => http.GetFromJsonAsync<List<QuestionAssignmentResponse>>(
            $"api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/users",
            ct);

    public async Task<FinalAnswerResponse?> TryGetFinalAnswerAsync(string bidId, string questionId, CancellationToken ct = default)
    {
        var response = await http.GetAsync(
            $"api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/final-answer",
            ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Failed to load final answer: {(int)response.StatusCode} {response.ReasonPhrase}");

        return await response.Content.ReadFromJsonAsync<FinalAnswerResponse>(ct);
    }

    public async Task SaveFinalAnswerAsync(
        string bidId,
        string questionId,
        string answerText,
        bool readyForSubmission,
        CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync(
            $"api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/final-answer",
            new UpdateFinalAnswerRequest
            {
                AnswerText = answerText ?? string.Empty,
                ReadyForSubmission = readyForSubmission
            },
            ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Failed to save final answer: {(int)response.StatusCode} {response.ReasonPhrase}");
    }

    public async Task<DraftCommentResponse> SetDraftCommentCompletionAsync(
        string bidId,
        string questionId,
        string draftId,
        string commentId,
        bool isComplete,
        CancellationToken ct = default)
    {
        var response = await http.PatchAsJsonAsync(
            $"api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/drafts/{Uri.EscapeDataString(draftId)}/comments/{Uri.EscapeDataString(commentId)}",
            new SetCommentCompletionRequest { IsComplete = isComplete },
            ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Failed to update draft comment completion: {(int)response.StatusCode} {response.ReasonPhrase}");

        return await response.Content.ReadFromJsonAsync<DraftCommentResponse>(ct)
               ?? throw new InvalidOperationException("Draft comment completion response was empty.");
    }

    public async Task<DraftCommentResponse> SetRedReviewCommentCompletionAsync(
        string bidId,
        string questionId,
        string commentId,
        bool isComplete,
        CancellationToken ct = default)
    {
        var response = await http.PatchAsJsonAsync(
            $"api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/red-review/comments/{Uri.EscapeDataString(commentId)}",
            new SetCommentCompletionRequest { IsComplete = isComplete },
            ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Failed to update red review comment completion: {(int)response.StatusCode} {response.ReasonPhrase}");

        return await response.Content.ReadFromJsonAsync<DraftCommentResponse>(ct)
               ?? throw new InvalidOperationException("Red review comment completion response was empty.");
    }

    public async Task<DraftCommentResponse> SetFinalAnswerCommentCompletionAsync(
        string bidId,
        string questionId,
        string commentId,
        bool isComplete,
        CancellationToken ct = default)
    {
        var response = await http.PatchAsJsonAsync(
            $"api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/final-answer/comments/{Uri.EscapeDataString(commentId)}",
            new SetCommentCompletionRequest { IsComplete = isComplete },
            ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Failed to update final answer comment completion: {(int)response.StatusCode} {response.ReasonPhrase}");

        return await response.Content.ReadFromJsonAsync<DraftCommentResponse>(ct)
               ?? throw new InvalidOperationException("Final answer comment completion response was empty.");
    }
}
