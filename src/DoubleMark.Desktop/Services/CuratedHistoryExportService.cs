using System.Globalization;
using System.IO;
using DoubleMark.Core.Export;
using DoubleMark.Core.Parsing;
using DoubleMark.Desktop.Views;

namespace DoubleMark.Desktop.Services;

public sealed class CuratedHistoryExportService
{
    private readonly MarkExportService _exportService = new();
    private readonly Gs1Parser _parser = new();

    public CuratedHistoryExportResult ExportItems(IEnumerable<ScanHistoryItem> items, string targetRoot)
    {
        Directory.CreateDirectory(targetRoot);
        var exported = 0;
        var skipped = 0;

        foreach (var item in items)
        {
            if (TryExportItem(item, targetRoot))
                exported++;
            else
                skipped++;
        }

        return new CuratedHistoryExportResult(exported, skipped, targetRoot);
    }

    private bool TryExportItem(ScanHistoryItem item, string targetRoot)
    {
        if (!string.IsNullOrEmpty(item.RawPayload))
        {
            var parse = _parser.Parse(item.RawPayload);
            if (parse.IsValid && parse.Code != null)
            {
                var result = _exportService.Save(new MarkExportRequest
                {
                    RawPayload = item.RawPayload,
                    ParseResult = parse,
                    Source = item.Source,
                    ExportRoot = targetRoot,
                    Timestamp = item.Timestamp
                });
                if (result.Success)
                    return true;
            }
        }

        return TryCopyFromSavedFolder(item, targetRoot);
    }

    private static bool TryCopyFromSavedFolder(ScanHistoryItem item, string targetRoot)
    {
        if (string.IsNullOrWhiteSpace(item.SavedFolder) || item.SavedFolder == "—")
            return false;
        if (!Directory.Exists(item.SavedFolder))
            return false;

        var dayFolder = Path.Combine(
            targetRoot,
            item.Timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(dayFolder);

        var serial = item.Serial is "—" or "" ? null : item.Serial;
        var extensions = new[] { ".json", ".png", ".txt", ".pdf" };
        var copied = 0;

        foreach (var ext in extensions)
        {
            var candidates = Directory.EnumerateFiles(item.SavedFolder, "*" + ext)
                .Where(path => serial == null
                               || Path.GetFileName(path).Contains(serial, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (candidates.Count == 0 && serial != null)
                continue;

            var source = candidates.Count > 0
                ? candidates.OrderByDescending(File.GetLastWriteTimeUtc).First()
                : Directory.EnumerateFiles(item.SavedFolder, "*" + ext)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();

            if (source == null)
                continue;

            var dest = Path.Combine(dayFolder, Path.GetFileName(source));
            dest = MakeUniquePath(dest);
            File.Copy(source, dest, overwrite: false);
            copied++;
        }

        return copied > 0;
    }

    private static string MakeUniquePath(string path)
    {
        if (!File.Exists(path))
            return path;

        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 1; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{name}_{i}{ext}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(dir, $"{name}_{Guid.NewGuid():N}{ext}");
    }
}

public sealed record CuratedHistoryExportResult(int Exported, int Skipped, string TargetRoot);
