namespace TalentSuite.Server.Bids.Data.Models;

public class QuestionDataModel
{
    public QuestionDataModel(string id)
    {
        Id = id;
    }

    private QuestionDataModel() // for deserialisation
    {
    }

    public string Id { get; set; }
    public string Category { get; set; }
    public string Number { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Length { get; set; } = "";
    public int Weighting { get; set; } // 0-100
    public bool Required { get; set; }
    public bool NiceToHave { get; set; }
}
