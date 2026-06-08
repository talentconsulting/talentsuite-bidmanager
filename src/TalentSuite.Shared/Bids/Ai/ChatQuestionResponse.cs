namespace TalentSuite.Shared.Bids.Ai;

public class ChatQuestionResponse
{
    public string Response { get; set; } = string.Empty;
    public string ThreadId { get; set; } = string.Empty;
    public List<ChatSourceReferenceResponse> Sources { get; set; } = new();
    public bool UsedSourcesOutsideBidLibrary { get; set; }
}
