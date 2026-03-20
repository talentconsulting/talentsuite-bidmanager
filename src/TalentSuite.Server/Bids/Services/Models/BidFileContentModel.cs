namespace TalentSuite.Server.Bids.Services.Models;

public sealed class BidFileContentModel
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public byte[] Content { get; set; } = Array.Empty<byte>();
}
