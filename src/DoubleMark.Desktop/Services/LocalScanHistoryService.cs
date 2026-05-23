using System.IO;
using DoubleMark.Core.Models;
using DoubleMark.Core.Parsing;
using DoubleMark.Desktop.Services.Cloud;
using DoubleMark.Desktop.Settings;
using DoubleMark.Desktop.Views;

namespace DoubleMark.Desktop.Services;

public sealed class LocalScanHistoryService
{
    private string? _lastIgnoredHash;
    private DateTime _lastIgnoredHashUtc = DateTime.MinValue;

    public IReadOnlyList<ScanHistoryItem> Load(AppSettings settings)
    {
        var fromFile = ScanHistoryStore.Load();
        var browseRoot = settings.EffectiveLocalHistoryBrowseDirectory;
        var fromFolder = Directory.Exists(browseRoot)
            ? ScanHistoryImporter.FromExports(browseRoot, ScanHistoryStore.MaxEntries)
            : Array.Empty<ScanHistoryItem>();

        var merged = ScanHistoryImporter.Merge(fromFile, fromFolder, ScanHistoryStore.MaxEntries);
        LoggingService.Info("ScanHistory",
            $"Local load file={fromFile.Count} folder={fromFolder.Count} merged={merged.Count} root={browseRoot}");
        return merged;
    }

    public ScanHistoryItem? Add(AppSettings settings, ScanHistoryItem item, ParseResult result)
    {
        if (!result.IsValid || result.Code == null)
            return null;

        var hash = CloudScanHistoryService.ComputeCodeHash(item.RawPayload);
        if (ShouldIgnoreRecentDuplicate(settings, hash))
        {
            LoggingService.Info("ScanHistory", "Local recent duplicate ignored");
            return null;
        }

        var current = ScanHistoryStore.Load().ToList();
        current.Insert(0, item);
        while (current.Count > ScanHistoryStore.MaxEntries)
            current.RemoveAt(current.Count - 1);

        ScanHistoryStore.Save(current);
        _lastIgnoredHash = hash;
        _lastIgnoredHashUtc = DateTime.UtcNow;
        LoggingService.Info("ScanHistory", "Local save count=" + current.Count);
        return item;
    }

    public bool Delete(AppSettings settings, ScanHistoryItem item)
    {
        var key = ScanHistoryItemBuilder.DedupeKey(item);
        var current = ScanHistoryStore.Load().ToList();
        var removed = current.RemoveAll(i => ScanHistoryItemBuilder.DedupeKey(i) == key);
        if (removed == 0)
            return false;

        ScanHistoryStore.Save(current);
        return true;
    }

    public void ClearStore()
    {
        ScanHistoryStore.Save(Array.Empty<ScanHistoryItem>());
        LoggingService.Info("ScanHistory", "Local store cleared");
    }

    private bool ShouldIgnoreRecentDuplicate(AppSettings settings, string hash)
    {
        if (settings.ScanHistoryDuplicateMode == ScanHistoryDuplicateMode.KeepAll)
            return false;

        return string.Equals(_lastIgnoredHash, hash, StringComparison.OrdinalIgnoreCase)
               && DateTime.UtcNow - _lastIgnoredHashUtc < TimeSpan.FromSeconds(2);
    }
}
