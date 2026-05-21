using DoubleMark.Core.Parsing;

namespace DoubleMark.Desktop.Services.Cloud;

public static class ScanHistoryMasking
{
    public static string BuildMaskedPreview(string rawPayload, int maxVisiblePrefix = 16)
    {
        if (string.IsNullOrEmpty(rawPayload))
            return "—";

        var normalized = Gs1BarcodeEncoding.NormalizeForParse(rawPayload).Payload;
        if (normalized.Length == 0)
            return "—";

        var visible = normalized.Length <= maxVisiblePrefix
            ? normalized
            : normalized[..maxVisiblePrefix];

        return visible + " ***";
    }
}
