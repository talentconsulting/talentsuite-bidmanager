using TalentSuite.Shared.Bids;

namespace TalentSuite.FrontEnd.Services;

public sealed class BidState
{
    public ParsedDocumentResponse? LastUpload { get; set; }
    public BidStage? SelectedStage { get; set; }
    public string? SourceDocumentName { get; set; }
    public string? SourceDocumentContentType { get; set; }
    public byte[]? SourceDocumentBytes { get; set; }

    public void Clear()
    {
        LastUpload = null;
        SelectedStage = null;
        SourceDocumentName = null;
        SourceDocumentContentType = null;
        SourceDocumentBytes = null;
    }
}
