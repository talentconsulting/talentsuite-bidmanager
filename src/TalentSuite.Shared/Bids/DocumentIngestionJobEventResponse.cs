namespace TalentSuite.Shared.Bids;

public sealed class DocumentIngestionJobEventResponse
{
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsComplete { get; set; }
    public bool IsError { get; set; }
    public ParsedDocumentResponse? Result { get; set; }
}
