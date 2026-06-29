using System.Text.Json;
using System.Text.Json.Serialization;

namespace DoubleMark.Crpt;

public static class CrptJson
{
    public static readonly JsonSerializerOptions Api = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static readonly JsonSerializerOptions Compact = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static string ToCompact<T>(T value) =>
        JsonSerializer.Serialize(value, Compact);
}
