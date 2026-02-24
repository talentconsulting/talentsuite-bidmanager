namespace TalentSuite.Shared.Bids;

public class CreateAssetResponse
{
    public CreateAssetResponse(string id)
    {
        Id = id;
    }
    public string Id { get; set; } = string.Empty; 
}