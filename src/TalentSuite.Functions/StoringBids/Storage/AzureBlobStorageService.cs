using Azure.Storage.Blobs;
using Azure.Identity;
using Microsoft.Extensions.Configuration;

namespace TalentSuite.Functions.StoringBids.Storage;

public sealed class AzureBlobStorageService(IConfiguration configuration) : IAzureBlobStorageService
{
    public async Task CheckConnectionAsync(CancellationToken ct = default)
    {
        var blobServiceClient = CreateBlobServiceClient("bidstorage");
        await blobServiceClient.GetPropertiesAsync(cancellationToken: ct);
    }

    public async Task WriteTextAsync(
        string containerName,
        string blobName,
        string content,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(containerName))
            throw new ArgumentException("Container name is required.", nameof(containerName));

        if (string.IsNullOrWhiteSpace(blobName))
            throw new ArgumentException("Blob name is required.", nameof(blobName));

        var blobServiceClient = CreateBlobServiceClient("bidstorage");
        var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
        await containerClient.CreateIfNotExistsAsync(cancellationToken: ct);

        var blobClient = containerClient.GetBlobClient(blobName);
        await blobClient.UploadAsync(
            BinaryData.FromString(content ?? string.Empty),
            overwrite: true,
            cancellationToken: ct);
    }

    public async Task<string?> ReadTextAsync(
        string containerName,
        string blobName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(containerName))
            throw new ArgumentException("Container name is required.", nameof(containerName));

        if (string.IsNullOrWhiteSpace(blobName))
            throw new ArgumentException("Blob name is required.", nameof(blobName));

        var blobServiceClient = CreateBlobServiceClient("bidstorage");
        var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobName);

        var exists = await blobClient.ExistsAsync(ct);
        if (!exists.Value)
            return null;

        var response = await blobClient.DownloadContentAsync(cancellationToken: ct);
        return response.Value.Content.ToString();
    }
    private BlobServiceClient CreateBlobServiceClient(string name)
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

            return new BlobServiceClient(new Uri(blobServiceUri), credential);
        }

        var connectionString = configuration.GetConnectionString(name);
        if (!string.IsNullOrWhiteSpace(connectionString))
            return new BlobServiceClient(connectionString);

        connectionString = configuration[$"ConnectionStrings:{name}"];
        if (!string.IsNullOrWhiteSpace(connectionString))
            return new BlobServiceClient(connectionString);

        throw new InvalidOperationException(
            $"Storage configuration for '{name}' was not found. Provide ConnectionStrings:{name} or BidStorage__blobServiceUri.");
    }
}
