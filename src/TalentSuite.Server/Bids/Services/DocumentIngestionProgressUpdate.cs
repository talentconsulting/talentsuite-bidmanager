namespace TalentSuite.Server.Bids.Services;

public sealed class DocumentIngestionProgressUpdate
{
    public string Status { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
