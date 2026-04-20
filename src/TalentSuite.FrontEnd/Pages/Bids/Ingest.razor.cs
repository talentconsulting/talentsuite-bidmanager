using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using TalentSuite.Shared;
using TalentSuite.Shared.Bids;
using TalentSuite.Shared.Users;

namespace TalentSuite.FrontEnd.Pages.Bids;

public partial class Ingest : ComponentBase, IAsyncDisposable
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
    protected string BusyMessage { get; set; } = "Processing document...";
    protected List<string> ProgressMessages { get; } = [];

    private string? _activeJobId;
    private bool _hasTerminalEvent;
    private CancellationTokenSource? _pollingCts;

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
        ProgressMessages.Clear();
        _hasTerminalEvent = false;
        _activeJobId = null;
        await CancelPollingAsync();

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
        BusyMessage = "Uploading document...";

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

            var res = await Http.PostAsync("api/document/jobs", content);

            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync();
                ErrorText = $"HTTP {(int)res.StatusCode} {res.ReasonPhrase}\n\n{body}";
                return;
            }

            var job = await res.Content.ReadFromJsonAsync<DocumentIngestionJobCreatedResponse>(SerialiserOptions.JsonOptions);
            if (job is null || string.IsNullOrWhiteSpace(job.JobId))
            {
                ErrorText = "Server did not return a document ingestion job id.";
                return;
            }

            _activeJobId = job.JobId;

            DraftState.SourceDocumentName = File.Name;
            DraftState.SourceDocumentContentType = File.ContentType;
            DraftState.SourceDocumentBytes = fileBytes;
            DraftState.SelectedStage = stage;

            BusyMessage = "Starting ingestion...";
            ProgressMessages.Add("Document uploaded.");
            await StartPollingFallbackAsync("Document uploaded. Polling ingestion status.");
        }
        catch (Exception ex)
        {
            ErrorText = ex.ToString();
            IsBusy = false;
        }
    }

    [JSInvokable]
    public async Task HandleIngestionEvent(string json)
    {
        var update = JsonSerializer.Deserialize<DocumentIngestionJobEventResponse>(json, SerialiserOptions.JsonOptions);
        if (update is null)
            return;

        if (!string.IsNullOrWhiteSpace(update.Message))
        {
            BusyMessage = update.Message;
            ProgressMessages.Add(update.Message);
            if (ProgressMessages.Count > 6)
                ProgressMessages.RemoveAt(0);
        }

        if (update.IsError)
        {
            _hasTerminalEvent = true;
            ErrorText = update.Message;
            IsBusy = false;
        }
        else if (update.IsComplete)
        {
            _hasTerminalEvent = true;
            if (update.Result is null)
            {
                ErrorText = "Document ingestion completed without a parsed result.";
                IsBusy = false;
            }
            else
            {
                DraftState.LastUpload = update.Result;
                IsBusy = false;
                await InvokeAsync(() => Nav.NavigateTo("/bids/ingestion-summary"));
            }
        }

        await InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public async Task HandleIngestionStreamError(string message)
    {
        await StartPollingFallbackAsync(message);
        await InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public async Task HandleIngestionStreamCompleted()
    {
        if (IsBusy && !_hasTerminalEvent)
            await StartPollingFallbackAsync("The live ingestion stream ended early. Continuing with status polling.");

        await InvokeAsync(StateHasChanged);
    }

    public async ValueTask DisposeAsync()
    {
        await CancelPollingAsync();
    }

    private async Task StartPollingFallbackAsync(string message)
    {
        if (_hasTerminalEvent || string.IsNullOrWhiteSpace(_activeJobId))
            return;

        BusyMessage = "Checking ingestion status...";
        AppendProgressMessage(message);
        await CancelPollingAsync();
        _pollingCts = new CancellationTokenSource();
        _ = PollJobUntilCompleteAsync(_activeJobId, _pollingCts.Token);
    }

    private async Task PollJobUntilCompleteAsync(string jobId, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var job = await Http.GetFromJsonAsync<DocumentIngestionJobStatusResponse>(
                    $"api/document/jobs/{Uri.EscapeDataString(jobId)}",
                    SerialiserOptions.JsonOptions,
                    ct);

                if (job is not null)
                {
                    if (!string.IsNullOrWhiteSpace(job.Message))
                    {
                        BusyMessage = job.Message;
                        AppendProgressMessage(job.Message);
                    }

                    if (job.IsError)
                    {
                        ErrorText = job.Message;
                        IsBusy = false;
                        return;
                    }

                    if (job.IsComplete)
                    {
                        _hasTerminalEvent = true;
                        if (job.Result is null)
                        {
                            ErrorText = "Document ingestion completed without a parsed result.";
                            IsBusy = false;
                            return;
                        }

                        DraftState.LastUpload = job.Result;
                        IsBusy = false;
                        await InvokeAsync(() => Nav.NavigateTo("/bids/ingestion-summary"));
                        return;
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            IsBusy = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task CancelPollingAsync()
    {
        if (_pollingCts is null)
            return;

        await _pollingCts.CancelAsync();
        _pollingCts.Dispose();
        _pollingCts = null;
    }

    private void AppendProgressMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        if (ProgressMessages.Count == 0 || !string.Equals(ProgressMessages[^1], message, StringComparison.Ordinal))
            ProgressMessages.Add(message);

        if (ProgressMessages.Count > 6)
            ProgressMessages.RemoveAt(0);
    }
}
