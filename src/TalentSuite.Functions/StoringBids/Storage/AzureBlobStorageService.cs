using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;

namespace TalentSuite.Functions.StoringBids.Storage;

public sealed class AzureBlobStorageService(IConfiguration configuration) : IAzureBlobStorageService
{
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

        var connectionString = GetRequiredConnectionString("bidstorage");
        var blobServiceClient = new BlobServiceClient(connectionString);
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

        var connectionString = GetRequiredConnectionString("bidstorage");
        var blobServiceClient = new BlobServiceClient(connectionString);
        var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobName);

        var exists = await blobClient.ExistsAsync(ct);
        if (!exists.Value)
            return null;

        var response = await blobClient.DownloadContentAsync(cancellationToken: ct);
        return response.Value.Content.ToString();
    }

    private string GetRequiredConnectionString(string name)
    {
        var connectionString = configuration.GetConnectionString(name);
        if (!string.IsNullOrWhiteSpace(connectionString))
            return connectionString;

        connectionString = configuration[$"ConnectionStrings:{name}"];
        if (!string.IsNullOrWhiteSpace(connectionString))
            return connectionString;

        throw new InvalidOperationException($"Connection string '{name}' was not found.");
    }
}
