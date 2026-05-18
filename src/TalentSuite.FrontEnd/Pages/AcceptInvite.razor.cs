using Microsoft.AspNetCore.Components;

namespace TalentSuite.FrontEnd.Pages;

public partial class AcceptInvite
{
    [Inject] public AcceptInviteApiClient ApiClient { get; set; } = default!;

    [Parameter]
    [SupplyParameterFromQuery(Name = "token")]
    public string? InvitationToken { get; set; }

    private bool IsLoading { get; set; } = true;
    private bool IsSuccess { get; set; }
    private bool IsAlreadyAccepted { get; set; }
    private string? ErrorText { get; set; }
    private bool IsSubmitting { get; set; }
    private string Username { get; set; } = string.Empty;
    private string Password { get; set; } = string.Empty;
    private string ConfirmPassword { get; set; } = string.Empty;

    protected override async Task OnParametersSetAsync()
    {
        IsLoading = true;
        ErrorText = null;
        IsAlreadyAccepted = false;

        if (string.IsNullOrWhiteSpace(InvitationToken))
        {
            ErrorText = "Invitation token is missing.";
            IsLoading = false;
            return;
        }

        var status = await ApiClient.ValidateTokenAsync(InvitationToken);
        if (status == "already_accepted")
        {
            IsAlreadyAccepted = true;
        }
        else if (status != "valid")
        {
            ErrorText = "This invitation link is invalid or has expired. Please contact your administrator.";
        }
        else if (string.IsNullOrWhiteSpace(Username))
        {
            Username = BuildDefaultUsername();
        }

        IsLoading = false;
    }

    private async Task RegisterAsync()
    {
        if (string.IsNullOrWhiteSpace(InvitationToken))
        {
            ErrorText = "Invitation token is missing.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Username))
        {
            ErrorText = "Username is required.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            ErrorText = "Password is required.";
            return;
        }

        if (!string.Equals(Password, ConfirmPassword, StringComparison.Ordinal))
        {
            ErrorText = "Passwords do not match.";
            return;
        }

        IsSubmitting = true;
        ErrorText = null;
        try
        {
            await ApiClient.RegisterInviteAsync(InvitationToken, Username.Trim(), Password);
            IsSuccess = true;
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    private string BuildDefaultUsername()
    {
        if (string.IsNullOrWhiteSpace(InvitationToken))
            return string.Empty;

        var trimmed = InvitationToken.Trim();
        var slice = trimmed.Length > 8 ? trimmed[..8] : trimmed;
        return $"user.{slice}".ToLowerInvariant();
    }
}
