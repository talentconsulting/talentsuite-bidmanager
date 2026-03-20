using System.Net.Http.Json;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TalentSuite.FrontEnd.Mappers;
using TalentSuite.FrontEnd.Services;
using TalentSuite.Shared.Bids;

namespace TalentSuite.FrontEnd.Pages.Bids;

public partial class IngestSummary : ComponentBase
{
    [Inject] public NavigationManager Nav { get; set; } = default!;
    [Inject] public HttpClient Http { get; set; } = default!;
    [Inject] public Services.BidState DraftState { get; set; } = default!;
    [Inject] public GlobalBannerState BannerState { get; set; } = default!;
    [Inject] public IJSRuntime JS { get; set; } = default!;

    [Inject] public BidMapper Mapper { get; set; } = default!;

    protected ParsedDocumentResponse? Upload => DraftState.LastUpload;

    protected ClientResponseModel Model { get; set; } = new();

    protected bool IsBusy { get; set; }
    protected string? ErrorText { get; set; }
    protected string? SuccessText { get; set; }

    protected override void OnInitialized()
    {
        // If no upload, nothing to show
        if (Upload is null) return;

        // Clone into local editable model
        Model = new ClientResponseModel(Upload);

        Model.Sort();
    }

    protected void OnRequiredChanged(int index, ChangeEventArgs e)
    {
        var isChecked = e.Value is bool b ? b : bool.TryParse(e.Value?.ToString(), out var parsed) && parsed;
        Model.Response.Questions[index].Required = isChecked;

        // Optional UX rule: if Required = true, NiceToHave = false
        if (isChecked) Model.Response.Questions[index].NiceToHave = false;
    }

    protected void OnNiceChanged(int index, ChangeEventArgs e)
    {
        var isChecked = e.Value is bool b ? b : bool.TryParse(e.Value?.ToString(), out var parsed) && parsed;
        Model.Response.Questions[index].NiceToHave = isChecked;

        // Optional UX rule: if NiceToHave = true, Required = false
        if (isChecked) Model.Response.Questions[index].Required = false;
    }

    protected async Task DeleteQuestionAsync(int index)
    {
        if (index < 0 || index >= Model.Response.Questions.Count)
            return;

        var question = Model.Response.Questions[index];
        var questionLabel = string.IsNullOrWhiteSpace(question.Number)
            ? question.Title
            : $"#{question.Number} {question.Title}";

        var confirmed = await JS.InvokeAsync<bool>(
            "confirm",
            $"Are you sure you want to remove question '{questionLabel}'?");

        if (!confirmed)
            return;

        Model.Response.Questions.RemoveAt(index);
        ReindexQuestions();
        EnsureActiveCategoryAfterRemoval();
    }

    private async Task SaveQuestions()
    {
        ErrorText = null;
        SuccessText = null;

        if (Upload is null)
        {
            ErrorText = "No bid upload found.";
            return;
        }

        IsBusy = true;
        try
        {
            var req = Mapper.ToRequest(Model.Response);
            if (DraftState.SelectedStage is null)
            {
                ErrorText = "No stage selected. Please go back and select Stage 1 or Stage 2.";
                return;
            }
            req.Stage = DraftState.SelectedStage.Value;

            var res = await Http.PostAsJsonAsync(
                $"api/bids",
                req);

            if (!res.IsSuccessStatusCode)
            {
                ErrorText = $"Failed to create the bid";
                return;
            }

            var created = await res.Content.ReadFromJsonAsync<CreatedId>();
            if (created is null || string.IsNullOrWhiteSpace(created.Result))
            {
                ErrorText = "Bid was created but no bid id was returned.";
                return;
            }

            await TryUploadSourceDocumentAsync(created.Result);
            DraftState.Clear();
            Nav.NavigateTo("/bids/manage/" + created.Result);
        }
        catch (Exception ex)
        {
            ErrorText = ex.ToString();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private class CreatedId
    {
        public string Result { get; set; }
    }

    private async Task TryUploadSourceDocumentAsync(string bidId)
    {
        var fileBytes = DraftState.SourceDocumentBytes;
        if (fileBytes is null || fileBytes.Length == 0)
            return;

        var fileName = string.IsNullOrWhiteSpace(DraftState.SourceDocumentName)
            ? "source-document.docx"
            : DraftState.SourceDocumentName!;
        var contentType = string.IsNullOrWhiteSpace(DraftState.SourceDocumentContentType)
            ? "application/octet-stream"
            : DraftState.SourceDocumentContentType!;

        using var content = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);

        var uploadResponse = await Http.PostAsync($"api/bids/{Uri.EscapeDataString(bidId)}/files", content);
        if (!uploadResponse.IsSuccessStatusCode)
        {
            _ = BannerState.ShowAsync(
                "Bid created, but the original uploaded file could not be attached automatically.",
                "alert-warning",
                durationMs: 5000);
        }
    }

    private void ReindexQuestions()
    {
        for (var i = 0; i < Model.Response.Questions.Count; i++)
            Model.Response.Questions[i].QuestionOrderIndex = i + 1;
    }

    private void EnsureActiveCategoryAfterRemoval()
    {
        if (Model.Response.Questions.Count == 0)
        {
            ActiveCategory = null;
            return;
        }

        var availableCategories = Model.Response.Questions
            .Select(q => string.IsNullOrWhiteSpace(q.Category) ? "Uncategorised" : q.Category!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x)
            .ToList();

        if (string.IsNullOrWhiteSpace(ActiveCategory) || !availableCategories.Contains(ActiveCategory))
            ActiveCategory = availableCategories[0];
    }
}
