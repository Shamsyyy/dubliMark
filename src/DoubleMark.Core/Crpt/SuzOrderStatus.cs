using System.Text.Json;

namespace DoubleMark.Core.Crpt;

/// <summary>
/// Parses SUZ 3.0 order status JSON into <see cref="SuzOrderRemoteStatus"/> (spec §5, §14.1).
/// </summary>
public static class SuzOrderStatus
{
    public static SuzOrderRemoteStatus ParseFromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ParseFromElement(doc.RootElement);
    }

    public static SuzOrderRemoteStatus ParseFromElement(JsonElement root)
    {
        foreach (var propertyName in new[] { "orderStatus", "bufferStatus", "status" })
        {
            if (root.TryGetProperty(propertyName, out var statusEl))
            {
                var mapped = SuzOrderRemoteStatusMapper.FromRemoteStatus(statusEl.GetString());
                if (mapped != SuzOrderRemoteStatus.Unknown)
                    return mapped;
            }
        }

        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                var mapped = ParseFromElement(item);
                if (mapped != SuzOrderRemoteStatus.Unknown)
                    return mapped;
            }
        }

        return SuzOrderRemoteStatus.Unknown;
    }
}
