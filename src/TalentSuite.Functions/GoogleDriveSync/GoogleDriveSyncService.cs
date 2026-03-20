using System.Security.Cryptography;
using Azure.Storage.Blobs;
using Azure.Identity;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace TalentSuite.Functions.GoogleDriveSync;

public sealed class GoogleDriveSyncService(
    IConfiguration configuration,
    IOptions<GoogleDriveSyncOptions> options,
    ILogger<GoogleDriveSyncService> logger)
    : IGoogleDriveSyncService
{
    public async Task CheckConnectionAsync(CancellationToken ct)
    {
        var configured = options.Value;
        if (!configured.Enabled)
            return;

        if (string.IsNullOrWhiteSpace(configured.DriveFolderId))
            throw new InvalidOperationException("GoogleDriveSync:DriveFolderId is required when GoogleDriveSync:Enabled is true.");

        var credentialJson = ResolveServiceAccountJson(configured);
        var credential = GoogleCredential.FromJson(credentialJson).CreateScoped(DriveService.Scope.DriveReadonly);
        var driveService = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "TalentSuite Google Drive Sync"
        });

        var getRequest = driveService.Files.Get(configured.DriveFolderId);
        getRequest.Fields = "id,name";
        getRequest.SupportsAllDrives = true;
        await getRequest.ExecuteAsync(ct);
    }

    public async Task<GoogleDriveSyncResult> SyncAsync(CancellationToken ct)
    {
        var configured = options.Value;
        if (!configured.Enabled)
            return new GoogleDriveSyncResult(0, 0, 0);

        if (string.IsNullOrWhiteSpace(configured.DriveFolderId))
            throw new InvalidOperationException("GoogleDriveSync:DriveFolderId is required when GoogleDriveSync:Enabled is true.");

        var credentialJson = ResolveServiceAccountJson(configured);
        var credential = GoogleCredential.FromJson(credentialJson).CreateScoped(DriveService.Scope.Drive);
        var driveService = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "TalentSuite Google Drive Sync"
        });

        var container = CreateBidStorageContainerClient(configured.SourceContainerName);
        if (!await container.ExistsAsync(ct))
        {
            logger.LogWarning(
                "Google Drive sync skipped because container '{ContainerName}' does not exist.",
                configured.SourceContainerName);
            return new GoogleDriveSyncResult(0, 0, 0);
        }

        var uploaded = 0;
        var updated = 0;
        var skipped = 0;

        await foreach (var blob in container.GetBlobsAsync(cancellationToken: ct))
        {
            var blobClient = container.GetBlobClient(blob.Name);
            var blobDownload = await blobClient.DownloadContentAsync(ct);
            var contentBytes = blobDownload.Value.Content.ToArray();
            var md5Hex = Convert.ToHexString(MD5.HashData(contentBytes)).ToLowerInvariant();
            var mimeType = blob.Properties.ContentType ?? "application/octet-stream";
            var pathSegments = blob.Name
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var fileName = pathSegments.LastOrDefault();
            if (string.IsNullOrWhiteSpace(fileName))
            {
                logger.LogWarning("Google Drive sync skipped blob with empty terminal path segment: {BlobName}", blob.Name);
                skipped++;
                continue;
            }

            var parentFolderId = await ResolveDriveParentFolderAsync(
                driveService,
                configured.DriveFolderId,
                blob.Name,
                ct);

            var driveFile = await FindExistingDriveFileAsync(driveService, parentFolderId, blob.Name, ct);
            if (driveFile is not null && string.Equals(driveFile.Md5Checksum, md5Hex, StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }

            await using var contentStream = new MemoryStream(contentBytes, writable: false);
            if (driveFile is null)
            {
                var createMetadata = new DriveFile
                {
                    Name = fileName,
                    Parents = [parentFolderId],
                    AppProperties = new Dictionary<string, string>
                    {
                        ["azureBlobPath"] = blob.Name
                    }
                };

                var createRequest = driveService.Files.Create(createMetadata, contentStream, mimeType);
                createRequest.Fields = "id,name";
                createRequest.SupportsAllDrives = true;
                await createRequest.UploadAsync(ct);
                uploaded++;
            }
            else
            {
                var updateMetadata = new DriveFile
                {
                    AppProperties = new Dictionary<string, string>
                    {
                        ["azureBlobPath"] = blob.Name
                    }
                };
                var updateRequest = driveService.Files.Update(updateMetadata, driveFile.Id, contentStream, mimeType);
                updateRequest.Fields = "id,name";
                updateRequest.SupportsAllDrives = true;
                await updateRequest.UploadAsync(ct);
                updated++;
            }
        }

        return new GoogleDriveSyncResult(uploaded, updated, skipped);
    }

    private static async Task<DriveFile?> FindExistingDriveFileAsync(
        DriveService driveService,
        string folderId,
        string blobName,
        CancellationToken ct)
    {
        var listRequest = driveService.Files.List();
        listRequest.Spaces = "drive";
        listRequest.PageSize = 1;
        listRequest.Fields = "files(id,name,md5Checksum)";
        listRequest.SupportsAllDrives = true;
        listRequest.IncludeItemsFromAllDrives = true;
        listRequest.Q =
            $"'{EscapeForQuery(folderId)}' in parents and trashed=false and appProperties has {{ key='azureBlobPath' and value='{EscapeForQuery(blobName)}' }}";

        var result = await listRequest.ExecuteAsync(ct);
        return result.Files?.FirstOrDefault();
    }

    private static async Task<string> ResolveDriveParentFolderAsync(
        DriveService driveService,
        string rootFolderId,
        string blobName,
        CancellationToken ct)
    {
        var segments = blobName
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length <= 1)
            return rootFolderId;

        var parentFolderId = rootFolderId;
        foreach (var segment in segments[..^1])
            parentFolderId = await GetOrCreateFolderAsync(driveService, parentFolderId, segment, ct);

        return parentFolderId;
    }

    private static async Task<string> GetOrCreateFolderAsync(
        DriveService driveService,
        string parentFolderId,
        string folderName,
        CancellationToken ct)
    {
        var listRequest = driveService.Files.List();
        listRequest.Spaces = "drive";
        listRequest.PageSize = 1;
        listRequest.Fields = "files(id,name)";
        listRequest.SupportsAllDrives = true;
        listRequest.IncludeItemsFromAllDrives = true;
        listRequest.Q =
            $"'{EscapeForQuery(parentFolderId)}' in parents and trashed=false and mimeType='application/vnd.google-apps.folder' and name='{EscapeForQuery(folderName)}'";

        var result = await listRequest.ExecuteAsync(ct);
        var existingFolder = result.Files?.FirstOrDefault();
        if (existingFolder is not null && !string.IsNullOrWhiteSpace(existingFolder.Id))
            return existingFolder.Id;

        var folderMetadata = new DriveFile
        {
            Name = folderName,
            Parents = [parentFolderId],
            MimeType = "application/vnd.google-apps.folder"
        };

        var createRequest = driveService.Files.Create(folderMetadata);
        createRequest.Fields = "id,name";
        createRequest.SupportsAllDrives = true;

        var createdFolder = await createRequest.ExecuteAsync(ct);
        if (string.IsNullOrWhiteSpace(createdFolder.Id))
            throw new InvalidOperationException($"Failed to create Google Drive folder '{folderName}'.");

        return createdFolder.Id;
    }

    private static string ResolveServiceAccountJson(GoogleDriveSyncOptions configured)
    {
        if (!string.IsNullOrWhiteSpace(configured.ServiceAccountJson))
            return configured.ServiceAccountJson;

        if (!string.IsNullOrWhiteSpace(configured.ServiceAccountJsonBase64))
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(configured.ServiceAccountJsonBase64));

        throw new InvalidOperationException(
            "Google Drive service account credentials are missing. Set GoogleDriveSync:ServiceAccountJson or GoogleDriveSync:ServiceAccountJsonBase64.");
    }

    private static string EscapeForQuery(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'");

    private BlobContainerClient CreateBidStorageContainerClient(string containerName)
    {
        var blobServiceUri = configuration["BidStorage:blobServiceUri"]
                             ?? configuration["BidStorage__blobServiceUri"]
                             ?? Environment.GetEnvironmentVariable("BidStorage__blobServiceUri");
        if (!string.IsNullOrWhiteSpace(blobServiceUri))
        {
            var clientId = configuration["BidStorage:clientId"]
                           ?? configuration["BidStorage:clientID"]
                           ?? configuration["BidStorage__clientId"]
                           ?? configuration["BidStorage__clientID"]
                           ?? Environment.GetEnvironmentVariable("BidStorage__clientId")
                           ?? Environment.GetEnvironmentVariable("BidStorage__clientID");
            var credential = string.IsNullOrWhiteSpace(clientId)
                ? new DefaultAzureCredential()
                : new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    ManagedIdentityClientId = clientId
                });

            var blobServiceClient = new BlobServiceClient(new Uri(blobServiceUri), credential);
            return blobServiceClient.GetBlobContainerClient(containerName);
        }

        var connectionString = configuration["ConnectionStrings:bidstorage"];
        if (!string.IsNullOrWhiteSpace(connectionString))
            return new BlobContainerClient(connectionString, containerName);

        throw new InvalidOperationException(
            "Storage configuration for bidstorage was not found. Provide ConnectionStrings:bidstorage or BidStorage__blobServiceUri.");
    }
}
