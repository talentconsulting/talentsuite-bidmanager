namespace TalentSuite.Functions;

public sealed class EmailOptions
{
    public const string SectionName = "InviteEmail";

    public bool Enabled { get; set; }
    public string FrontendBaseUrl { get; set; } = "https://localhost:5173";
    public string FromEmail { get; set; } = string.Empty;
    public string FromDisplayName { get; set; } = "TalentSuite";
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public bool SmtpEnableSsl { get; set; } = true;
    public string SmtpUsername { get; set; } = string.Empty;
    public string SmtpPassword { get; set; } = string.Empty;
}
