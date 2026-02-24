using Riok.Mapperly.Abstractions;
using TalentSuite.Shared.Bids;

namespace TalentSuite.FrontEnd.Mappers;

[Mapper]
public partial class BidMapper
{
    public partial CreateBidRequest ToRequest(ParsedDocumentResponse source);
    public partial CreateQuestionRequest ToRequest(ParsedQuestionResponse source);
}
