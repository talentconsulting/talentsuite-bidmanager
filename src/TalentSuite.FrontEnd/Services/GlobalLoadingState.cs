namespace TalentSuite.FrontEnd.Services;

public sealed class GlobalLoadingState
{
    private int _pendingCount;

    public bool IsBusy => _pendingCount > 0;

    public event Action? OnChange;

    public void Begin()
    {
        _pendingCount++;
        OnChange?.Invoke();
    }

    public void End()
    {
        if (_pendingCount > 0)
            _pendingCount--;

        OnChange?.Invoke();
    }
}
