namespace TalentSuite.FrontEnd.Services;

public sealed class LoadingHttpMessageHandler : DelegatingHandler
{
    private readonly GlobalLoadingState _loadingState;

    public LoadingHttpMessageHandler(GlobalLoadingState loadingState)
    {
        _loadingState = loadingState;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _loadingState.Begin();
        try
        {
            return await base.SendAsync(request, cancellationToken);
        }
        finally
        {
            _loadingState.End();
        }
    }
}
