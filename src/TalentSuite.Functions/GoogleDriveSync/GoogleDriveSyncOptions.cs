namespace TalentSuite.Functions.GoogleDriveSync;

public sealed class GoogleDriveSyncOptions
{
    public const string SectionName = "GoogleDriveSync";

    public bool Enabled { get; set; }
    public string SourceContainerName { get; set; } = "bidlibrary";
    public string DriveFolderId { get; set; } = string.Empty;
    public string? ServiceAccountJson { get; set; }
    public string? ServiceAccountJsonBase64 { get; set; }
}
