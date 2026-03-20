using TalentSuite.Shared.Bids;

namespace TalentSuite.Functions.StoringBids.BidLibrary;

public interface IBidLibraryWriter
{
    Task WriteBidAsync(
        BidResponse bid,
        IReadOnlyDictionary<string, string> finalAnswerTextByQuestionId,
        CancellationToken ct = default);
}
