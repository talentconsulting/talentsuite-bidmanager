using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Options;
using TalentSuite.Functions.GoogleDriveSync;
using TalentSuite.Functions.StoringBids.Storage;

namespace TalentSuite.Functions.Health;

public sealed class HealthCheckFunction(
    IAzureBlobStorageService blobStorageService,
    IGoogleDriveSyncService googleDriveSyncService,
    IOptions<GoogleDriveSyncOptions> googleDriveSyncOptions)
{
    [Function("HealthCheck")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData request,
        CancellationToken ct)
    {
        string storageStatus;
        string googleStatus;

        try
        {
            await blobStorageService.CheckConnectionAsync(ct);
            storageStatus = "reachable";
        }
        catch (Exception ex)
        {
            storageStatus = "unreachable";
            return await WriteUnhealthyResponseAsync(
                request,
                storageStatus,
                googleDriveSyncOptions.Value.Enabled ? "unknown" : "disabled",
                "storage",
                ex.Message);
        }

        if (!googleDriveSyncOptions.Value.Enabled)
        {
            googleStatus = "disabled";
        }
        else
        {
            try
            {
                await googleDriveSyncService.CheckConnectionAsync(ct);
                googleStatus = "reachable";
            }
            catch (Exception ex)
            {
                googleStatus = "unreachable";
                return await WriteUnhealthyResponseAsync(
                    request,
                    storageStatus,
                    googleStatus,
                    "googleDrive",
                    ex.Message);
            }
        }

        var response = request.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync(
            $"{{\"status\":\"ok\",\"storage\":\"{storageStatus}\",\"googleDrive\":\"{googleStatus}\"}}",
            ct);
        return response;
    }

    private static async Task<HttpResponseData> WriteUnhealthyResponseAsync(
        HttpRequestData request,
        string storageStatus,
        string googleStatus,
        string failedDependency,
        string errorMessage)
    {
        var unavailableResponse = request.CreateResponse(HttpStatusCode.ServiceUnavailable);
        unavailableResponse.Headers.Add("Content-Type", "application/json");
        await unavailableResponse.WriteStringAsync(
            $"{{\"status\":\"unhealthy\",\"storage\":\"{storageStatus}\",\"googleDrive\":\"{googleStatus}\",\"failedDependency\":\"{failedDependency}\",\"error\":\"{EscapeJson(errorMessage)}\"}}");
        return unavailableResponse;
    }

    private static string EscapeJson(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
