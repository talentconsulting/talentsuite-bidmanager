using TalentSuite.Shared.Bids;

namespace TalentSuite.Functions.StoringBids.BidLibrary;

public interface IBidLibraryApiClient
{
    Task<BidResponse?> GetBidAsync(string bidId, CancellationToken ct = default);

    Task<string> GetFinalAnswerTextAsync(string bidId, string questionId, CancellationToken ct = default);
}
