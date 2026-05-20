using DoubleMark.Core.Export;
using DoubleMark.Core.History;
using DoubleMark.Core.Parsing;
using DoubleMark.Desktop.Views;

namespace DoubleMark.Desktop.Services;

public static class ScanHistoryImporter
{
    public static IReadOnlyList<ScanHistoryItem> FromExports(string exportRoot, int maxEntries)
    {
        var records = ExportScanHistoryImporter.Import(exportRoot, maxEntries);
        return records.Select(ToHistoryItem).ToList();
    }

    public static IReadOnlyList<ScanHistoryItem> Merge(
        IReadOnlyList<ScanHistoryItem> primary,
        IReadOnlyList<ScanHistoryItem> secondary,
        int maxEntries)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var merged = new List<ScanHistoryItem>(Math.Min(maxEntries, primary.Count + secondary.Count));

        void AddRange(IEnumerable<ScanHistoryItem> items)
        {
            foreach (var item in items)
            {
                if (merged.Count >= maxEntries)
                    return;

                var key = DedupeKey(item);
                if (!seen.Add(key))
                    continue;

                merged.Add(item);
            }
        }

        AddRange(primary);
        AddRange(secondary);
        return merged;
    }

    private static string DedupeKey(ScanHistoryItem item) =>
        item.Timestamp.ToString("yyyyMMddHHmmss") + "|" + item.RawPayload;

    private static ScanHistoryItem ToHistoryItem(ExportScanRecord record)
    {
        var escapedRaw = MarkExportService.EscapePayload(record.RawPayload);
        var escapedNorm = MarkExportService.EscapePayload(record.NormalizedPayload);

        return new ScanHistoryItem
        {
            Timestamp = record.Timestamp,
            Status = "Успешно",
            StatusKind = UiStatusKind.Success,
            Gtin = string.IsNullOrWhiteSpace(record.Gtin) ? "—" : record.Gtin,
            Serial = string.IsNullOrWhiteSpace(record.Serial) ? "—" : record.Serial,
            Ai91 = record.Ai91 ?? "—",
            Ai92 = record.Ai92 ?? "—",
            Ai93 = record.Ai93 ?? "—",
            GsCount = record.GsCount.ToString(),
            Source = record.Source,
            CodeType = ShortCodeType(record.CodeType),
            RawEscaped = escapedRaw,
            RawPayload = record.RawPayload,
            NormalizedEscaped = escapedNorm,
            RawHex = Gs1BarcodeEncoding.ToHex(record.RawPayload),
            Error = "",
            SavedFolder = record.ExportDirectory,
            Template = "—",
            Printer = "—",
            PrintStatus = "—",
            PreviewImage = null
        };
    }

    private static string ShortCodeType(string codeType) =>
        codeType switch
        {
            "Short" => "Короткий",
            "Standard" => "Стандартный",
            "Extended" => "Расширенный",
            _ => codeType
        };
}