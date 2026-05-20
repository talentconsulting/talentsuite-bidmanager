namespace TalentSuite.Shared.Bids;

public class UpdateQuestionRequest
{
    public string Category { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Length { get; set; } = string.Empty;
    public int Weighting { get; set; }
    public bool Required { get; set; }
    public bool NiceToHave { get; set; }
}
