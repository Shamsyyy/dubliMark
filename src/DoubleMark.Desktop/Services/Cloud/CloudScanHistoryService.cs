using System.Security.Cryptography;
using System.Text;
using DoubleMark.Core.Export;
using DoubleMark.Core.Models;
using DoubleMark.Core.Parsing;
using DoubleMark.Core.Print;
using DoubleMark.Desktop.Services.Account;
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

    private readonly SupabaseClientFactory _clientFactory;
    private readonly Func<AccountUser?> _getCurrentUser;
    private readonly Func<AppSettings> _getSettings;

    private string? _lastIgnoredHash;
    private DateTime _lastIgnoredHashUtc = DateTime.MinValue;

    public CloudScanHistoryService(
        SupabaseClientFactory clientFactory,
        Func<AccountUser?> getCurrentUser,
        Func<AppSettings> getSettings)
    {
        _clientFactory = clientFactory;
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

            var row = new UserScanHistoryRow
            {
                UserId = user.Id,
                RawCode = rawPayload,
                CodeHash = hash,
                Source = source,
                GsCount = gsCount,
                HasAi01 = !string.IsNullOrWhiteSpace(code.Gtin),
                HasAi21 = !string.IsNullOrWhiteSpace(code.Serial),
                HasAi91 = code.VerificationKey != null,
                HasAi92 = code.VerificationCode != null,
                Gtin = code.Gtin,
                Serial = code.Serial,
                ScannedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            var insert = await _clientFactory.GetClient()
                .From<UserScanHistoryRow>()
                .Insert(row);

            var saved = insert.Models.FirstOrDefault();
            if (saved == null)
            {
                LoggingService.Warn("ScanHistory", "Add failed: empty insert response");
                return (null, false);
            }

            _lastIgnoredHash = hash;
            _lastIgnoredHashUtc = DateTime.UtcNow;

            var count = await GetHistoryCountAsync();
            LoggingService.Info("ScanHistory", "Add success count=" + count + "/" + MaxScanHistory);
            if (count >= MaxScanHistory)
                LoggingService.Info("ScanHistory", "Limit " + MaxScanHistory + " reached; oldest records trimmed on server");

            return (ToItem(
                saved,
                status,
                parseError,
                maskedPreview,
                rawEscaped,
                normalizedEscaped,
                rawHex,
                templateName,
                printerName,
                printStatus,
                savedFolder), false);
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
            var result = await _clientFactory.GetClient()
                .From<UserScanHistoryRow>()
                .Where(row => row.UserId == user.Id)
                .Get();

            var items = result.Models
                .OrderByDescending(row => row.ScannedAt ?? row.CreatedAt ?? DateTime.MinValue)
                .Take(limit)
                .Select(row => ToItem(row))
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
            var result = await _clientFactory.GetClient()
                .From<UserScanHistoryRow>()
                .Where(row => row.UserId == user.Id)
                .Get();

            return result.Models.Count;
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
            await _clientFactory.GetClient()
                .From<UserScanHistoryRow>()
                .Where(row => row.Id == id && row.UserId == user.Id)
                .Delete();

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
            await _clientFactory.GetClient()
                .From<UserScanHistoryRow>()
                .Where(row => row.UserId == user.Id)
                .Delete();

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
        UserScanHistoryRow row,
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
        var scannedAt = row.ScannedAt ?? row.CreatedAt ?? DateTime.UtcNow;
        var displayStatus = status ?? "Успешно";
        return new ScanHistoryItem
        {
            CloudId = row.Id,
            Timestamp = scannedAt.ToLocalTime(),
            Status = displayStatus,
            StatusKind = UiStatusKind.Success,
            Gtin = row.Gtin ?? "—",
            Serial = row.Serial ?? "—",
            Ai91 = row.HasAi91 ? "✓" : "—",
            Ai92 = row.HasAi92 ? "✓" : "—",
            Ai93 = "—",
            HasAi01 = row.HasAi01,
            HasAi21 = row.HasAi21,
            HasAi91Flag = row.HasAi91,
            HasAi92Flag = row.HasAi92,
            GsCount = (row.GsCount ?? 0).ToString(),
            Source = row.Source ?? "—",
            CodeType = "Full",
            RawEscaped = rawEscaped ?? ScanHistoryMasking.BuildMaskedPreview(row.RawCode),
            RawPayload = row.RawCode,
            NormalizedEscaped = normalizedEscaped ?? ScanHistoryMasking.BuildMaskedPreview(row.RawCode),
            RawHex = rawHex ?? "—",
            Error = parseError ?? "",
            SavedFolder = savedFolder ?? "—",
            Template = templateName ?? "—",
            Printer = printerName ?? "—",
            PrintStatus = printStatus ?? "—",
            MaskedPreview = maskedPreview ?? ScanHistoryMasking.BuildMaskedPreview(row.RawCode),
            PreviewImage = null
        };
    }

}
