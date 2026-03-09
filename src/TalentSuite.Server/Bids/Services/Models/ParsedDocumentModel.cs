namespace TalentSuite.Server.Bids.Services.Models;

public class ParsedDocumentModel
{
    public string? Company { get; set; }
    public string? Summary { get; set; }
    public string? KeyInformation { get; set; }
    public string? Budget { get; set; }
    public string? DeadlineForQualifying { get; set; }
    public string? DeadlineForSubmission { get; set; }
    public string? LengthOfContract { get; set; }
    
    public List<ParsedQuestionModel> Questions { get; set; }
}
