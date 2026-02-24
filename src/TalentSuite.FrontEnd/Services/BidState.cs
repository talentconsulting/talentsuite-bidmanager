using TalentSuite.Shared.Bids;

namespace TalentSuite.FrontEnd.Services;

public sealed class BidState
{
    public ParsedDocumentResponse? LastUpload { get; set; }
    public BidStage? SelectedStage { get; set; }
    public void Clear()
    {
        LastUpload = null;
        SelectedStage = null;
    }
}
