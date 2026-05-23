using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DoubleMark.Desktop.Views;

namespace DoubleMark.Desktop.Services;

public sealed class ScanHistoryEntry
{
    public DateTime Timestamp { get; set; }
    public string Status { get; set; } = "Ожидание";
    public UiStatusKind StatusKind { get; set; } = UiStatusKind.Neutral;
    public string Gtin { get; set; } = "—";
    public string Serial { get; set; } = "—";
    public string Ai91 { get; set; } = "—";
    public string Ai92 { get; set; } = "—";
    public string Ai93 { get; set; } = "—";
    public string GsCount { get; set; } = "—";
    public string Source { get; set; } = "—";
    public string CodeType { get; set; } = "—";
    public string RawEscaped { get; set; } = "—";
    public string RawPayload { get; set; } = "";
    public string NormalizedEscaped { get; set; } = "—";
    public string RawHex { get; set; } = "—";
    public string Error { get; set; } = "";
    public string SavedFolder { get; set; } = "—";
    public string Template { get; set; } = "—";
    public string Printer { get; set; } = "—";
    public string PrintStatus { get; set; } = "—";
}

internal sealed class ScanHistoryFile
{
    public int Version { get; set; } = 1;
    public List<ScanHistoryEntry> Entries { get; set; } = new();
}

public static class ScanHistoryStore
{
    /// <summary>Local scan log cap (~100k × ~2 KB ≈ hundreds of MB on disk; fine for production PCs).</summary>
    public const int MaxEntries = 100_000;

    private static readonly ReaderWriterLockSlim _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static string HistoryDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DoubleMark");

    private static string HistoryFilePath => Path.Combine(HistoryDirectory, "scan-history.json");

    public static IReadOnlyList<ScanHistoryItem> Load()
    {
        _lock.EnterReadLock();
        try
        {
            if (!File.Exists(HistoryFilePath))
                return Array.Empty<ScanHistoryItem>();

            var json = File.ReadAllText(HistoryFilePath);
            var file = JsonSerializer.Deserialize<ScanHistoryFile>(json, JsonOptions);
            if (file?.Entries == null || file.Entries.Count == 0)
                return Array.Empty<ScanHistoryItem>();

            return file.Entries
                .OrderByDescending(e => e.Timestamp)
                .Take(MaxEntries)
                .Select(ToItem)
                .ToList();
        }
        catch (Exception ex)
        {
            LoggingService.Error("ScanHistory", "Load failed", ex);
            TryArchiveCorruptedHistory();
            return Array.Empty<ScanHistoryItem>();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public static void Save(IReadOnlyList<ScanHistoryItem> items)
    {
        _lock.EnterWriteLock();
        try
        {
            Directory.CreateDirectory(HistoryDirectory);
            var entries = items
                .Take(MaxEntries)
                .Select(ToEntry)
                .ToList();

            var file = new ScanHistoryFile { Version = 1, Entries = entries };
            var json = JsonSerializer.Serialize(file, JsonOptions);
            var tempPath = HistoryFilePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, HistoryFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            LoggingService.Error("ScanHistory", "Save failed", ex);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    private static ScanHistoryItem ToItem(ScanHistoryEntry entry) => new()
    {
        Timestamp = entry.Timestamp == default ? DateTime.Now : entry.Timestamp,
        Status = entry.Status,
        StatusKind = entry.StatusKind,
        Gtin = entry.Gtin,
        Serial = entry.Serial,
        Ai91 = entry.Ai91,
        Ai92 = entry.Ai92,
        Ai93 = entry.Ai93,
        GsCount = entry.GsCount,
        Source = entry.Source,
        CodeType = entry.CodeType,
        RawEscaped = entry.RawEscaped,
        RawPayload = entry.RawPayload,
        NormalizedEscaped = entry.NormalizedEscaped,
        RawHex = entry.RawHex,
        Error = entry.Error,
        SavedFolder = entry.SavedFolder,
        Template = entry.Template,
        Printer = entry.Printer,
        PrintStatus = entry.PrintStatus,
        PreviewImage = null
    };

    private static ScanHistoryEntry ToEntry(ScanHistoryItem item) => new()
    {
        Timestamp = item.Timestamp,
        Status = item.Status,
        StatusKind = item.StatusKind,
        Gtin = item.Gtin,
        Serial = item.Serial,
        Ai91 = item.Ai91,
        Ai92 = item.Ai92,
        Ai93 = item.Ai93,
        GsCount = item.GsCount,
        Source = item.Source,
        CodeType = item.CodeType,
        RawEscaped = item.RawEscaped,
        RawPayload = item.RawPayload,
        NormalizedEscaped = item.NormalizedEscaped,
        RawHex = item.RawHex,
        Error = item.Error,
        SavedFolder = item.SavedFolder,
        Template = item.Template,
        Printer = item.Printer,
        PrintStatus = item.PrintStatus
    };

    private static void TryArchiveCorruptedHistory()
    {
        try
        {
            if (!File.Exists(HistoryFilePath))
                return;

            var backup = Path.Combine(HistoryDirectory, $"scan-history.corrupted.{DateTime.Now:yyyyMMddHHmmss}.json");
            File.Move(HistoryFilePath, backup, overwrite: false);
            LoggingService.Warn("ScanHistory", "Corrupted history archived to " + backup);
        }
        catch (Exception ex)
        {
            LoggingService.Error("ScanHistory", "Failed to archive corrupted history", ex);
        }
    }
}
