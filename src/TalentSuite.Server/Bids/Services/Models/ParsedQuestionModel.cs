namespace TalentSuite.Server.Bids.Services.Models;

public class ParsedQuestionModel
{
    public string Category { get; set; }
    public string Number { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Length { get; set; } = "";
    public int Weighting { get; set; }
    public bool Required { get; set; }
    public bool NiceToHave { get; set; }
}
