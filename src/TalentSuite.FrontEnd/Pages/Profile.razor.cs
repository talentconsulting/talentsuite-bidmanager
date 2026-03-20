using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using System.Security.Claims;
using TalentSuite.Shared.Users;

namespace TalentSuite.FrontEnd.Pages;

public class ProfilePage : ComponentBase
{
    [Inject] public ProfileApiClient ApiClient { get; set; } = default!;
    [Inject] public AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;
    [Inject] public IAccessTokenProvider AccessTokenProvider { get; set; } = default!;

    protected bool IsLoading { get; set; } = true;
    protected string? ErrorText { get; set; }
    protected CurrentUserProfileResponse? Profile { get; set; }
    protected bool IsAuthenticated { get; set; }
    protected string? AuthName { get; set; }
    protected string? SubjectClaim { get; set; }
    protected string? PreferredUsernameClaim { get; set; }
    protected string? EmailClaim { get; set; }
    protected List<string> RoleClaims { get; set; } = new();
    protected string AccessTokenStatus { get; set; } = "Unknown";
    protected DateTimeOffset? AccessTokenExpiresUtc { get; set; }
    protected int? IdentityDebugStatusCode { get; set; }
    protected string? IdentityDebugBody { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await LoadClientAuthDebugAsync();
        await LoadProfileAsync();
        await LoadIdentityDebugAsync();
    }

    private async Task LoadProfileAsync()
    {
        IsLoading = true;
        ErrorText = null;
        try
        {
            Profile = await ApiClient.GetMyProfileAsync();
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            Profile = null;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadClientAuthDebugAsync()
    {
        var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        var user = authState.User;
        IsAuthenticated = user.Identity?.IsAuthenticated ?? false;
        AuthName = user.Identity?.Name;
        SubjectClaim = user.FindFirst("sub")?.Value;
        PreferredUsernameClaim = user.FindFirst("preferred_username")?.Value;
        EmailClaim = user.FindFirst("email")?.Value;
        RoleClaims = user.FindAll(ClaimTypes.Role)
            .Concat(user.FindAll("role"))
            .Select(c => c.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var tokenResult = await AccessTokenProvider.RequestAccessToken();
        AccessTokenStatus = tokenResult.Status.ToString();
        if (tokenResult.TryGetToken(out var token))
            AccessTokenExpiresUtc = token.Expires;
    }

    private async Task LoadIdentityDebugAsync()
    {
        try
        {
            var result = await ApiClient.GetMyIdentityDebugAsync();
            IdentityDebugStatusCode = (int)result.StatusCode;
            IdentityDebugBody = result.Body;
        }
        catch (Exception ex)
        {
            IdentityDebugStatusCode = null;
            IdentityDebugBody = $"Failed to call /api/users/me-identity-debug: {ex.Message}";
        }
    }
}
