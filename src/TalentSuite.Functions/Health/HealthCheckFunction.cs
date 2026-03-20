using System.Net;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using TalentSuite.Functions.GoogleDriveSync;
using TalentSuite.Functions.StoringBids.Storage;

namespace TalentSuite.Functions.Health;

public sealed class HealthCheckFunction(
    IConfiguration configuration,
    IAzureBlobStorageService blobStorageService,
    IGoogleDriveSyncService googleDriveSyncService,
    IOptions<GoogleDriveSyncOptions> googleDriveSyncOptions)
{
    [Function("HealthCheck")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData request,
        CancellationToken ct)
    {
        string infraStorageStatus;
        string bidStorageStatus;
        string googleStatus;

        try
        {
            await CheckInfrastructureStorageConnectionAsync(ct);
            infraStorageStatus = "reachable";
        }
        catch (Exception ex)
        {
            infraStorageStatus = "unreachable";
            return await WriteUnhealthyResponseAsync(
                request,
                infraStorageStatus,
                "unknown",
                googleDriveSyncOptions.Value.Enabled ? "unknown" : "disabled",
                "infraStorage",
                ex.Message);
        }

        try
        {
            await blobStorageService.CheckConnectionAsync(ct);
            bidStorageStatus = "reachable";
        }
        catch (Exception ex)
        {
            bidStorageStatus = "unreachable";
            return await WriteUnhealthyResponseAsync(
                request,
                infraStorageStatus,
                bidStorageStatus,
                googleDriveSyncOptions.Value.Enabled ? "unknown" : "disabled",
                "bidStorage",
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
                    infraStorageStatus,
                    bidStorageStatus,
                    googleStatus,
                    "googleDrive",
                    ex.Message);
            }
        }

        var response = request.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync(
            $"{{\"status\":\"ok\",\"infraStorage\":\"{infraStorageStatus}\",\"bidStorage\":\"{bidStorageStatus}\",\"googleDrive\":\"{googleStatus}\"}}",
            ct);
        return response;
    }

    private async Task CheckInfrastructureStorageConnectionAsync(CancellationToken ct)
    {
        var blobServiceClient = CreateInfrastructureBlobServiceClient();
        await blobServiceClient.GetPropertiesAsync(cancellationToken: ct);
    }

    private BlobServiceClient CreateInfrastructureBlobServiceClient()
    {
        var blobServiceUri = configuration["AzureWebJobsStorage:blobServiceUri"]
                             ?? configuration["AzureWebJobsStorage__blobServiceUri"]
                             ?? Environment.GetEnvironmentVariable("AzureWebJobsStorage__blobServiceUri");
        if (!string.IsNullOrWhiteSpace(blobServiceUri))
        {
            var clientId = configuration["AzureWebJobsStorage:clientId"]
                           ?? configuration["AzureWebJobsStorage:clientID"]
                           ?? configuration["AzureWebJobsStorage__clientId"]
                           ?? configuration["AzureWebJobsStorage__clientID"]
                           ?? Environment.GetEnvironmentVariable("AzureWebJobsStorage__clientId")
                           ?? Environment.GetEnvironmentVariable("AzureWebJobsStorage__clientID");
            var credential = string.IsNullOrWhiteSpace(clientId)
                ? new DefaultAzureCredential()
                : new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    ManagedIdentityClientId = clientId
                });

            return new BlobServiceClient(new Uri(blobServiceUri), credential);
        }

        var connectionString = configuration["AzureWebJobsStorage"]
                               ?? Environment.GetEnvironmentVariable("AzureWebJobsStorage");
        if (!string.IsNullOrWhiteSpace(connectionString))
            return new BlobServiceClient(connectionString);

        throw new InvalidOperationException(
            "Infrastructure storage configuration was not found. Provide AzureWebJobsStorage or AzureWebJobsStorage__blobServiceUri.");
    }

    private static async Task<HttpResponseData> WriteUnhealthyResponseAsync(
        HttpRequestData request,
        string infraStorageStatus,
        string bidStorageStatus,
        string googleStatus,
        string failedDependency,
        string errorMessage)
    {
        var unavailableResponse = request.CreateResponse(HttpStatusCode.ServiceUnavailable);
        unavailableResponse.Headers.Add("Content-Type", "application/json");
        await unavailableResponse.WriteStringAsync(
            $"{{\"status\":\"unhealthy\",\"infraStorage\":\"{infraStorageStatus}\",\"bidStorage\":\"{bidStorageStatus}\",\"googleDrive\":\"{googleStatus}\",\"failedDependency\":\"{failedDependency}\",\"error\":\"{EscapeJson(errorMessage)}\"}}");
        return unavailableResponse;
    }

    private static string EscapeJson(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
