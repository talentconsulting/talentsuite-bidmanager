namespace TalentSuite.Server.Bids.Data.Models;

public sealed class BidFileDataModel
{
    public string Id { get; set; } = string.Empty;
    public string BidId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    public byte[] Content { get; set; } = Array.Empty<byte>();
}
