namespace TalentSuite.Functions.GoogleDriveSync;

public interface IGoogleDriveSyncService
{
    Task CheckConnectionAsync(CancellationToken ct);

    Task<GoogleDriveSyncResult> SyncAsync(CancellationToken ct);
}

public sealed record GoogleDriveSyncResult(int UploadedCount, int UpdatedCount, int SkippedCount);
