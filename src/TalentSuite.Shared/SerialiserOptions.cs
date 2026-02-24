using System.Text.Json;
using System.Text.Json.Serialization;

namespace TalentSuite.Shared;

public class SerialiserOptions
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
