namespace TalentSuite.Server.Bids.Services.Models;

public class CreateQuestionModel
{
    public string? Id { get; set; }
    public int QuestionOrderIndex { get; set; }
    public string Number { get; set; }
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public string Length { get; set; } = "";
    public int Weighting { get; set; } // 0-100
    public bool Required { get; set; }
    public bool NiceToHave { get; set; }
}
