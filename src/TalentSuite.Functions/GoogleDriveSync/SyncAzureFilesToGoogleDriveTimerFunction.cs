using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TalentSuite.Functions.GoogleDriveSync;

public sealed class SyncAzureFilesToGoogleDriveTimerFunction(
    IGoogleDriveSyncService syncService,
    IOptions<GoogleDriveSyncOptions> options,
    ILogger<SyncAzureFilesToGoogleDriveTimerFunction> logger)
{
    [Function("SyncAzureFilesToGoogleDriveTimerFunction")]
    public async Task Run([TimerTrigger("0 */30 * * * *")] TimerInfo timerInfo, CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            logger.LogDebug("Google Drive sync timer fired, but sync is disabled.");
            return;
        }

        var result = await syncService.SyncAsync(ct);
        logger.LogInformation(
            "Google Drive sync completed. Uploaded={UploadedCount}, Updated={UpdatedCount}, Skipped={SkippedCount}",
            result.UploadedCount,
            result.UpdatedCount,
            result.SkippedCount);
    }
}
