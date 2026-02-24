namespace TalentSuite.Server.Bids.Services.Models;

public class DraftModel
{
    public DraftModel(string id)
    {
        Id = id;
    }

    private DraftModel() // for deserialisation
    {
    }

    public string Id { get; set; }
    public string Response { get; set; } = string.Empty;
    public List<DraftCommentModel> Comments { get; set; } = new();
}
