using TalentSuite.Shared.Bids;

namespace TalentSuite.FrontEnd;

public class ClientResponseModel
{
    public ParsedDocumentResponse Response { get; set; }

    public ClientResponseModel()
    {
        
    }
    
    public ClientResponseModel(ParsedDocumentResponse response)
    {
        Response = response;
    }

    public void Sort()
    {
        Response.Questions = Response.Questions
            .OrderBy(q => q.Category)
            .ThenBy(q => Int16.Parse(q.Number))
            .ToList();
    }
}