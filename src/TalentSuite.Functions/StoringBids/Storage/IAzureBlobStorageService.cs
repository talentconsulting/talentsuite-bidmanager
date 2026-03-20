namespace TalentSuite.Functions.StoringBids.Storage;

public interface IAzureBlobStorageService
{
    Task CheckConnectionAsync(CancellationToken ct = default);

    Task WriteTextAsync(
        string containerName,
        string blobName,
        string content,
        CancellationToken ct = default);

    Task<string?> ReadTextAsync(
        string containerName,
        string blobName,
        CancellationToken ct = default);
}
