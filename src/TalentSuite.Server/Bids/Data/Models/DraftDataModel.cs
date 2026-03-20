namespace TalentSuite.Server.Bids.Data.Models;

public class DraftDataModel
{
    public DraftDataModel(string id)
    {
        Id = id;
    }

    private DraftDataModel() // for deserialisation
    {
    }

    public string Id { get; set; }
    public string Response { get; set; } = string.Empty;
    public List<DraftCommentDataModel> Comments { get; set; } = new();
}
