using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using TalentSuite.Shared.Bids;
using TalentSuite.Shared.Bids.List;
using TalentSuite.Shared.Users;

namespace TalentSuite.FrontEnd.Pages.Bids;

public partial class Home
{
    [Inject] public HttpClient Http { get; set; } = default!;

    private bool IsLoading { get; set; }
    private string? ErrorText { get; set; }
    private bool IsAdminUser { get; set; }
    private string SelectedStatusFilter { get; set; } = string.Empty;

    private PagedBidListResponse Bids { get; set; } = new();

    private IReadOnlyList<BidListItemResponse> FilteredItems
    {
        get
        {
            if (Bids.Items is null || Bids.Items.Count == 0)
                return Array.Empty<BidListItemResponse>();

            if (!Enum.TryParse<BidStatus>(SelectedStatusFilter, true, out var parsedStatus))
                return Bids.Items;

            return Bids.Items
                .Where(item => item.Status == parsedStatus)
                .ToList();
        }
    }
    
    // Shows a small window around current page (e.g., 1..5)
    private IEnumerable<int> PageNumbersToShow
    {
        get
        {
            const int window = 2; // pages either side of current
            var start = Math.Max(1, Bids.CurrentPage - window);
            var end = Math.Min(Bids.TotalPages, Bids.CurrentPage + window);

            // If near edges, try to expand window a bit
            while (end - start < window * 2 && (start > 1 || end < Bids.TotalPages))
            {
                if (start > 1) start--;
                if (end < Bids.TotalPages) end++;
            }

            for (int p = start; p <= end; p++)
                yield return p;
        }
    }

    protected override async Task OnInitializedAsync()
    {
        await LoadAuthorisationAsync();
        if (!IsAdminUser)
        {
            Bids = new PagedBidListResponse();
            return;
        }

        await LoadPageAsync(Bids.CurrentPage);
    }

    private async Task LoadAuthorisationAsync()
    {
        try
        {
            var auth = await Http.GetFromJsonAsync<CurrentUserAuthorisationResponse>("api/users/me-authorisation");
            IsAdminUser = auth?.IsAdmin ?? false;
        }
        catch
        {
            IsAdminUser = false;
        }
    }

    private async Task LoadPageAsync(int page)
    {
        IsLoading = true;
        ErrorText = null;

        try
        {
            // expected API shape:
            // GET api/bids?page=1&pageSize=10
            // -> { items: [...], page: 1, pageSize: 10, totalCount: 123 }
            var url = $"api/bids?page={page}&pageSize={10}";
            var result = await Http.GetFromJsonAsync<PagedBidListResponse>(url);

            if (result is null)
            {
                Bids = new PagedBidListResponse();
                return;
            }

            Bids = result;
        }
        catch (Exception ex)
        {
            ErrorText = ex.ToString();
            Bids = new PagedBidListResponse();
        }
        finally
        {
            IsLoading = false;
        }
    }
    private void ManageBid(string id)
    {
        Nav.NavigateTo($"/bids/manage/{Uri.EscapeDataString(id)}");
    } 
    private async Task GoToPage(int page)
    {
        page = Math.Clamp(page, 1, Bids.TotalPages);
        await LoadPageAsync(page);
    }

    private async Task PrevPage()
    {
        if (Bids.CurrentPage > 1)
            await LoadPageAsync(Bids.CurrentPage - 1);
    }

    private async Task NextPage()
    {
        if (Bids.CurrentPage < Bids.TotalPages)
            await LoadPageAsync(Bids.CurrentPage + 1);
    }
}
