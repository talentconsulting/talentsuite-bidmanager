namespace TalentSuite.Shared.Bids;

public sealed class DocumentIngestionJobStatusResponse
{
    public string JobId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public BidStage Stage { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsComplete { get; set; }
    public bool IsError { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public ParsedDocumentResponse? Result { get; set; }
}
