namespace TalentSuite.FrontEnd.Services;

public sealed class GlobalBannerState
{
    private CancellationTokenSource? _cts;

    public string? Message { get; private set; }
    public string CssClass { get; private set; } = "alert-danger";
    public bool IsVisible => !string.IsNullOrWhiteSpace(Message);

    public event Action? OnChange;

    public Task ShowAsync(string message, string cssClass = "alert-danger", int durationMs = 3000)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        Message = message;
        CssClass = cssClass;
        OnChange?.Invoke();

        if (durationMs <= 0)
            return Task.CompletedTask;

        return AutoHideAsync(durationMs, _cts.Token);
    }

    public void Clear()
    {
        _cts?.Cancel();
        _cts = null;
        Message = null;
        OnChange?.Invoke();
    }

    private async Task AutoHideAsync(int durationMs, CancellationToken token)
    {
        try
        {
            await Task.Delay(durationMs, token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        Message = null;
        OnChange?.Invoke();
    }
}
