namespace TalentSuite.Shared.Bids.List;

public class PagedBidListResponse : PagedResult<BidListItemResponse>
{
    public PagedBidListResponse()
    {
        CurrentPage = 1;
    }
    public int TotalPages => (TotalCount + PageSize - 1) / PageSize;
    
    public int FirstItem => TotalCount == 0 ? 0 : ((CurrentPage - 1) * PageSize) + 1;
    public  int LastItem => TotalCount == 0 ? 0 : Math.Min(CurrentPage * PageSize, TotalCount);
}