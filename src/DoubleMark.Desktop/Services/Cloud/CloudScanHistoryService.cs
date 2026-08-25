using System.Security.Cryptography;
using System.Text;
using DoubleMark.Core.Export;
using DoubleMark.Core.Models;
using DoubleMark.Core.Parsing;
using DoubleMark.Core.Print;
using DoubleMark.Desktop.Services.Account;
using DoubleMark.Desktop.Services.Api;
using DoubleMark.Desktop.Settings;
using DoubleMark.Desktop.Views;
namespace DoubleMark.Desktop.Services.Cloud;

public enum ScanHistoryDuplicateMode
{
    KeepAll,
    IgnoreRecentDuplicates
}

public sealed class CloudScanHistoryService
{
    public const int MaxScanHistory = 1000;
    private static readonly TimeSpan RecentDuplicateWindow = TimeSpan.FromSeconds(2);

    private readonly DoubleMarkApiClient _api;
    private readonly Func<AccountUser?> _getCurrentUser;
    private readonly Func<AppSettings> _getSettings;

    private string? _lastIgnoredHash;
    private DateTime _lastIgnoredHashUtc = DateTime.MinValue;

    public CloudScanHistoryService(
        DoubleMarkApiClient api,
        Func<AccountUser?> getCurrentUser,
        Func<AppSettings> getSettings)
    {
        _api = api;
        _getCurrentUser = getCurrentUser;
        _getSettings = getSettings;
    }

    public int GetHistoryLimit() => MaxScanHistory;

    public static bool IsValidForCloudHistory(ParseResult result) =>
        result.IsValid
        && result.Code != null
        && result.Code.CodeType == MarkingCodeType.Full
        && !string.IsNullOrWhiteSpace(result.Code.Gtin)
        && !string.IsNullOrWhiteSpace(result.Code.Serial)
        && result.Code.VerificationKey != null
        && result.Code.VerificationCode != null;

    public static string ComputeCodeHash(string rawPayload)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawPayload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public async Task<(ScanHistoryItem? Item, bool DuplicateIgnored)> AddScanAsync(
        ParseResult result,
        string rawPayload,
        string source,
        MarkExportResult? exportResult,
        PrintPipelineResult? printResult,
        int? imageGsCount,
        string parseError,
        string maskedPreview,
        string rawEscaped,
        string normalizedEscaped,
        string rawHex,
        string templateName,
        string printerName,
        string printStatus,
        string savedFolder)
    {
        var user = _getCurrentUser();
        if (user == null)
            return (null, false);

        if (!IsValidForCloudHistory(result))
        {
            LoggingService.Info("ScanHistory", "Skip cloud save: scan is not a valid full Chestny ZNAK code");
            return (null, false);
        }

        var code = result.Code!;
        var normalized = exportResult?.NormalizedPayload
                         ?? code.RawData
                         ?? Gs1BarcodeEncoding.NormalizeForParse(rawPayload).Payload;
        var hash = ComputeCodeHash(rawPayload);

        if (ShouldIgnoreRecentDuplicate(hash))
        {
            LoggingService.Info("ScanHistory", "Recent duplicate ignored hash=" + hash[..Math.Min(12, hash.Length)] + "...");
            return (null, true);
        }

        var gsCount = imageGsCount ?? Gs1BarcodeEncoding.CountGs(normalized);
        var status = result.InfoMessages.Count > 0 ? "Предупреждение" : "Успешно";

        try
        {
            LoggingService.Info("ScanHistory", "Add started source=" + source + " length=" + rawPayload.Length);

            var saved = await _api.AddScanHistoryAsync(new
            {
                rawCode = rawPayload,
                codeHash = hash,
                source,
                gsCount,
                hasAi01 = !string.IsNullOrWhiteSpace(code.Gtin),
                hasAi21 = !string.IsNullOrWhiteSpace(code.Serial),
                hasAi91 = code.VerificationKey != null,
                hasAi92 = code.VerificationCode != null,
                gtin = code.Gtin,
                serial = code.Serial,
                scannedAt = DateTime.UtcNow
            });

            if (saved == null)
            {
                LoggingService.Warn("ScanHistory", "Add failed: empty insert response");
                return (null, false);
            }

            _lastIgnoredHash = hash;
            _lastIgnoredHashUtc = DateTime.UtcNow;
            var usage = await GetHistoryUsageAsync();
            LoggingService.Info("ScanHistory", "Add success count=" + usage.Count + "/" + MaxScanHistory);
            return (ToItemFromApi(saved, status, parseError, maskedPreview, rawEscaped, normalizedEscaped, rawHex, templateName, printerName, printStatus, savedFolder), false);
        }
        catch (Exception ex)
        {
            LoggingService.Error("ScanHistory", "Add failed", ex);
            return (null, false);
        }
    }

    public async Task<IReadOnlyList<ScanHistoryItem>> GetHistoryAsync(int limit = MaxScanHistory)
    {
        var user = _getCurrentUser();
        if (user == null)
            return Array.Empty<ScanHistoryItem>();

        try
        {
            var rows = await _api.GetScanHistoryAsync() ?? new List<DoubleMarkApiClient.ScanHistoryDto>();
            var items = rows
                .OrderByDescending(row => row.ScannedAt)
                .Take(limit)
                .Select(row => ToItemFromApi(row))
                .ToList();
            LoggingService.Info("ScanHistory", "Loaded count=" + items.Count.ToString() + "/" + MaxScanHistory);
            return items;
        }
        catch (Exception ex)
        {
            LoggingService.Error("ScanHistory", "Load failed", ex);
            return Array.Empty<ScanHistoryItem>();
        }
    }

    public async Task<int> GetHistoryCountAsync()
    {
        var user = _getCurrentUser();
        if (user == null)
            return 0;

        try
        {
            var usage = await _api.GetScanHistoryCountAsync();
            return usage?.Count ?? 0;
        }
        catch (Exception ex)
        {
            LoggingService.Error("ScanHistory", "Count failed", ex);
            return 0;
        }
    }

    public async Task<(int Count, int Limit)> GetHistoryUsageAsync()
    {
        var count = await GetHistoryCountAsync();
        return (count, MaxScanHistory);
    }

    public async Task<bool> DeleteHistoryItemAsync(string id)
    {
        var user = _getCurrentUser();
        if (user == null || !Guid.TryParse(id, out _))
            return false;

        try
        {
            await _api.DeleteScanHistoryItemAsync(Guid.Parse(id));
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.Error("ScanHistory", "Delete item failed", ex);
            return false;
        }
    }

    public async Task<bool> ClearHistoryAsync()
    {
        var user = _getCurrentUser();
        if (user == null)
            return false;

        try
        {
            await _api.ClearScanHistoryAsync();
            LoggingService.Info("ScanHistory", "History cleared for user");
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.Error("ScanHistory", "Clear failed", ex);
            return false;
        }
    }

    private bool ShouldIgnoreRecentDuplicate(string hash)
    {
        var settings = _getSettings();
        var mode = settings.ScanHistoryDuplicateMode;
        if (mode == ScanHistoryDuplicateMode.KeepAll)
            return false;

        return string.Equals(_lastIgnoredHash, hash, StringComparison.OrdinalIgnoreCase)
               && DateTime.UtcNow - _lastIgnoredHashUtc < RecentDuplicateWindow;
    }

    private static ScanHistoryItem ToItem(
        string id,
        string userId,
        string rawCode,
        string? source,
        int? gsCount,
        bool hasAi01,
        bool hasAi21,
        bool hasAi91,
        bool hasAi92,
        string? gtin,
        string? serial,
        DateTime? scannedAtUtc,
        DateTime? createdAtUtc,
        string? status = null,
        string? parseError = null,
        string? maskedPreview = null,
        string? rawEscaped = null,
        string? normalizedEscaped = null,
        string? rawHex = null,
        string? templateName = null,
        string? printerName = null,
        string? printStatus = null,
        string? savedFolder = null)
    {
        var scannedAt = scannedAtUtc ?? createdAtUtc ?? DateTime.UtcNow;
        var displayStatus = status ?? "Успешно";
        return new ScanHistoryItem
        {
            CloudId = id,
            Timestamp = scannedAt.ToLocalTime(),
            Status = displayStatus,
            StatusKind = UiStatusKind.Success,
            Gtin = gtin ?? "—",
            Serial = serial ?? "—",
            Ai91 = hasAi91 ? "✓" : "—",
            Ai92 = hasAi92 ? "✓" : "—",
            Ai93 = "—",
            HasAi01 = hasAi01,
            HasAi21 = hasAi21,
            HasAi91Flag = hasAi91,
            HasAi92Flag = hasAi92,
            GsCount = (gsCount ?? 0).ToString(),
            Source = source ?? "—",
            CodeType = "Full",
            RawEscaped = rawEscaped ?? ScanHistoryMasking.BuildMaskedPreview(rawCode),
            RawPayload = rawCode,
            NormalizedEscaped = normalizedEscaped ?? ScanHistoryMasking.BuildMaskedPreview(rawCode),
            RawHex = rawHex ?? "—",
            Error = parseError ?? "",
            SavedFolder = savedFolder ?? "—",
            Template = templateName ?? "—",
            Printer = printerName ?? "—",
            PrintStatus = printStatus ?? "—",
            MaskedPreview = maskedPreview ?? ScanHistoryMasking.BuildMaskedPreview(rawCode),
            PreviewImage = null
        };
    }

    private static ScanHistoryItem ToItemFromApi(
        DoubleMarkApiClient.ScanHistoryDto row,
        string? status = null,
        string? parseError = null,
        string? maskedPreview = null,
        string? rawEscaped = null,
        string? normalizedEscaped = null,
        string? rawHex = null,
        string? templateName = null,
        string? printerName = null,
        string? printStatus = null,
        string? savedFolder = null)
    {
        return ToItem(
            row.Id.ToString(),
            row.UserId.ToString(),
            row.RawCode,
            row.Source,
            row.GsCount,
            row.HasAi01,
            row.HasAi21,
            row.HasAi91,
            row.HasAi92,
            row.Gtin,
            row.Serial,
            row.ScannedAt,
            row.CreatedAt,
            status,
            parseError,
            maskedPreview,
            rawEscaped,
            normalizedEscaped,
            rawHex,
            templateName,
            printerName,
            printStatus,
            savedFolder);
    }

}
