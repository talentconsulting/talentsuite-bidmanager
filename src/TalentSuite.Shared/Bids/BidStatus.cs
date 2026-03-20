using System.Text.Json.Serialization;

namespace TalentSuite.Shared.Bids;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BidStatus
{
    Underway,
    Cancelled,
    Submitted,
    Won,
    Lost
}
