using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using TalentSuite.Shared;
using TalentSuite.Shared.Bids;
using TalentSuite.Shared.Users;

namespace TalentSuite.FrontEnd.Pages.Bids;

public partial class Ingest : ComponentBase
{
    [Inject] public HttpClient Http { get; set; } = default!;
    [Inject] public NavigationManager Nav { get; set; } = default!;
    [Inject] public Services.BidState DraftState { get; set; } = default!;

    
    protected IBrowserFile? File { get; set; }
    protected string? FileName { get; set; }
    protected long FileSizeBytes { get; set; }

    protected bool IsBusy { get; set; }
    protected string? ErrorText { get; set; }
    protected string? SelectedStageValue { get; set; }
    protected bool IsAdminUser { get; set; }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var auth = await Http.GetFromJsonAsync<CurrentUserAuthorisationResponse>("api/users/me-authorisation");
            IsAdminUser = auth?.IsAdmin ?? false;
        }
        catch
        {
            IsAdminUser = false;
        }
    }

    protected void OnFileChanged(InputFileChangeEventArgs e)
    {
        ErrorText = null;
        File = e.File;
        FileName = File.Name;
        FileSizeBytes = File.Size;
    }

    protected void OnStageChanged(ChangeEventArgs e)
    {
        SelectedStageValue = e.Value?.ToString();
    }

    protected async Task Submit()
    {
        ErrorText = null;

        if (!IsAdminUser)
        {
            ErrorText = "You do not have permission to create bids.";
            return;
        }

        if (File is null)
        {
            ErrorText = "Please choose a Word or Excel document (.doc, .docx, .xls, .xlsx).";
            return;
        }

        if (!Enum.TryParse<BidStage>(SelectedStageValue, true, out var stage))
        {
            ErrorText = "Please select a stage.";
            return;
        }

        IsBusy = true;

        try
        {
            using var content = new MultipartFormDataContent();
            await using var stream = File.OpenReadStream(maxAllowedSize: 25 * 1024 * 1024);
            await using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            var fileBytes = memory.ToArray();

            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType =
                new MediaTypeHeaderValue(File.ContentType);

            content.Add(fileContent, "file", File.Name);
            content.Add(new StringContent(stage.ToString()), "stage");

            var res = await Http.PostAsync("api/document", content);
            var body = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
            {
                ErrorText = $"HTTP {(int)res.StatusCode} {res.ReasonPhrase}\n\n{body}";
                return;
            }

            var upload = JsonSerializer.Deserialize<ParsedDocumentResponse>(
                body, SerialiserOptions.JsonOptions);

            if (upload is null)
            {
                ErrorText = "Server returned an empty response.";
                return;
            }

            DraftState.LastUpload = upload;
            DraftState.SelectedStage = stage;
            DraftState.SourceDocumentName = File.Name;
            DraftState.SourceDocumentContentType = File.ContentType;
            DraftState.SourceDocumentBytes = fileBytes;

            Nav.NavigateTo("/bids/ingestion-summary");
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
}
