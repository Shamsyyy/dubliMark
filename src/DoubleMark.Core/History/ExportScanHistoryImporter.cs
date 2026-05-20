using System.Globalization;
using System.Text.Json;

namespace DoubleMark.Core.History;

public sealed record ExportScanRecord(
    DateTime Timestamp,
    string Source,
    string RawPayload,
    string NormalizedPayload,
    string Gtin,
    string Serial,
    string? Ai91,
    string? Ai92,
    string? Ai93,
    int GsCount,
    string CodeType,
    string ExportDirectory);

/// <summary>
/// Rebuilds scan history items from local export JSON files (yyyy-MM-dd folders).
/// </summary>
public static class ExportScanHistoryImporter
{
    public static IReadOnlyList<ExportScanRecord> Import(string exportRoot, int maxEntries)
    {
        if (maxEntries <= 0 || string.IsNullOrWhiteSpace(exportRoot) || !Directory.Exists(exportRoot))
            return Array.Empty<ExportScanRecord>();

        var results = new List<ExportScanRecord>(Math.Min(maxEntries, 4096));
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var jsonPath in EnumerateExportJsonFiles(exportRoot))
        {
            if (results.Count >= maxEntries)
                break;

            if (!TryParseExportJson(jsonPath, out var record))
                continue;

            var key = record.RawPayload + "\u001e" + record.Timestamp.ToString("O", CultureInfo.InvariantCulture);
            if (!seen.Add(key))
                continue;

            results.Add(record);
        }

        results.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
        return results;
    }

    private static IEnumerable<string> EnumerateExportJsonFiles(string exportRoot)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(exportRoot, "*.json", SearchOption.AllDirectories);
        }
        catch
        {
            yield break;
        }

        string[] snapshot;
        try
        {
            snapshot = files
                .Select(path => (Path: path, Time: File.GetLastWriteTimeUtc(path)))
                .OrderByDescending(x => x.Time)
                .Select(x => x.Path)
                .ToArray();
        }
        catch
        {
            yield break;
        }

        foreach (var path in snapshot)
            yield return path;
    }

    public static bool TryParseExportJson(string jsonPath, out ExportScanRecord record)
    {
        record = null!;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (!root.TryGetProperty("rawPayload", out var rawEl))
                return false;

            var raw = rawEl.GetString();
            if (string.IsNullOrEmpty(raw))
                return false;

            var normalized = root.TryGetProperty("normalizedPayload", out var normEl)
                ? normEl.GetString() ?? raw
                : raw;

            if (!TryReadTimestamp(root, jsonPath, out var timestamp))
                return false;

            var source = root.TryGetProperty("source", out var sourceEl)
                ? sourceEl.GetString() ?? "Export"
                : "Export";

            var gtin = ReadString(root, "gtin") ?? "—";
            var serial = ReadString(root, "serial") ?? "—";
            var gsCount = root.TryGetProperty("gsCount", out var gsEl) && gsEl.TryGetInt32(out var gs)
                ? gs
                : 0;
            var codeType = ReadString(root, "codeType") ?? "—";

            record = new ExportScanRecord(
                timestamp,
                source,
                raw,
                normalized,
                gtin,
                serial,
                ReadString(root, "ai91"),
                ReadString(root, "ai92"),
                ReadString(root, "ai93"),
                gsCount,
                codeType,
                Path.GetDirectoryName(jsonPath) ?? "");

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadTimestamp(JsonElement root, string jsonPath, out DateTime timestamp)
    {
        if (root.TryGetProperty("timestamp", out var tsEl))
        {
            var text = tsEl.GetString();
            if (!string.IsNullOrWhiteSpace(text)
                && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
            {
                timestamp = dto.LocalDateTime;
                return true;
            }
        }

        try
        {
            timestamp = File.GetLastWriteTime(jsonPath);
            return true;
        }
        catch
        {
            timestamp = default;
            return false;
        }
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;
}
