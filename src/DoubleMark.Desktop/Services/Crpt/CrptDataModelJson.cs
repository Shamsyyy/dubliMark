using System.Text.Json;
using System.Text.Json.Serialization;

namespace DoubleMark.Desktop.Services.Crpt;

/// <summary>
/// JSON options for §6.3 order/code data models (in-memory and future persistence).
/// </summary>
public static class CrptDataModelJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
