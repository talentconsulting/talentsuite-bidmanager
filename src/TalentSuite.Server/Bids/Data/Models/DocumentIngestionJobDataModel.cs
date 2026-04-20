using TalentSuite.Shared.Bids;

namespace TalentSuite.Server.Bids.Data.Models;

public sealed class DocumentIngestionJobDataModel
{
    public string JobId { get; set; } = string.Empty;
    public string OwnerUserKey { get; set; } = string.Empty;
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
