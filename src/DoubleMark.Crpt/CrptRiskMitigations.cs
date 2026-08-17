using DoubleMark.Core.Crpt;

namespace DoubleMark.Crpt;

/// <summary>
/// Encoded mitigations for CRPT integration risks (spec §15 table).
/// </summary>
public static class CrptRiskMitigations
{
    public const string ApiUsageLimitHeaderName = "API-Usage-Limit";

    public const string CryptoProWindowsDependencyNote =
        "Подпись УКЭП выполняется через CryptoPro CAdESCOM и поддерживается только в Windows.";

    public const string ConnectionIdExpiredUserMessage =
        "Срок действия Connection ID истёк. Перевыпустите устройство в личном кабинете СУЗ и обновите Connection ID в настройках.";

    public const string TokenExpiredUserMessage =
        "Срок действия токена True API истёк. Нажмите «Продлить» или включите автопродление токена.";

    public const string UotLegalDisclaimer =
        "Интеграция с ЦРПТ выполняется от имени участника оборота (УОТ). Юридическая ответственность за заказ кодов маркировки, отчётность и ввод в оборот несёт УОТ.";

    public const string LargeCatalogMitigationNote =
        "Каталоги более 10 000 карточек: разбиение по диапазону дат / etagslist — фаза 2.";

    public const string AttributesPerProductGroupNote =
        "Атрибуты отчёта utilisation зависят от товарной группы — см. Appendix по ТГ в документации.";

    public static readonly TimeSpan NkRateLimitInitialBackoff = TimeSpan.FromSeconds(1);

    public const int NkRateLimitMaxRetryAttempts = 4;

    /// <summary>NK HTTP client timeout (large catalogs may need longer connect/read).</summary>
    public const int NkHttpTimeoutSeconds = 180;

    public const int NkConnectionRetryAttempts = 3;

    public static readonly TimeSpan NkConnectionRetryInitialBackoff = TimeSpan.FromSeconds(2);

    private static readonly IReadOnlyDictionary<string, int> DefaultTemplateIds =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [CrptProductGroup.Chemistry] = 46,
        };

    private static readonly CrptRiskKind[] AllRiskKinds =
    [
        CrptRiskKind.CryptoProWindowsOnly,
        CrptRiskKind.SuzUrlFromSettingsOnly,
        CrptRiskKind.AttributesPerProductGroup,
        CrptRiskKind.ConnectionIdExpiry,
        CrptRiskKind.TokenTenHourExpiry,
        CrptRiskKind.NkApiRateLimit,
        CrptRiskKind.LargeCatalogPhase2,
        CrptRiskKind.TemplateIdDefaults,
        CrptRiskKind.UotLegalDisclaimer,
    ];

    public static IReadOnlyList<CrptRiskKind> AllRisks => AllRiskKinds;

    public static string DescribeMitigation(CrptRiskKind risk) =>
        risk switch
        {
            CrptRiskKind.CryptoProWindowsOnly => CryptoProWindowsDependencyNote,
            CrptRiskKind.SuzUrlFromSettingsOnly => "SUZ URL берётся только из настроек (ЛК заказчика), без жёсткой подмены в коде.",
            CrptRiskKind.AttributesPerProductGroup => AttributesPerProductGroupNote,
            CrptRiskKind.ConnectionIdExpiry => ConnectionIdExpiredUserMessage,
            CrptRiskKind.TokenTenHourExpiry => TokenExpiredUserMessage,
            CrptRiskKind.NkApiRateLimit =>
                $"Backoff ({NkRateLimitInitialBackoff.TotalSeconds}s initial, {NkRateLimitMaxRetryAttempts} attempts) + заголовок {ApiUsageLimitHeaderName}.",
            CrptRiskKind.LargeCatalogPhase2 => LargeCatalogMitigationNote,
            CrptRiskKind.TemplateIdDefaults => "templateId по productGroup в настройках с fallback-значениями.",
            CrptRiskKind.UotLegalDisclaimer => UotLegalDisclaimer,
            _ => throw new ArgumentOutOfRangeException(nameof(risk), risk, null),
        };

    public static void EnsureWindowsForCryptoPro() =>
        CrptPlatformGuard.EnsureWindowsForCertificateOperations("CryptoPro UKEP signing");

    /// <summary>Returns configured SUZ URL as-is (no hardcoded suzgrid/suz2 override).</summary>
    public static string ResolveSuzBaseUrl(string configuredUrl)
    {
        if (string.IsNullOrWhiteSpace(configuredUrl))
            throw new ArgumentException("SUZ URL must be configured in CRPT settings (customer LK value).", nameof(configuredUrl));

        return configuredUrl.Trim();
    }

    public static int? ResolveTemplateId(
        string? productGroup,
        IReadOnlyDictionary<string, int>? configuredDefaults = null)
    {
        var group = CrptProductGroup.Normalize(productGroup);
        if (string.IsNullOrEmpty(group))
            return null;

        if (configuredDefaults is not null &&
            configuredDefaults.TryGetValue(group, out var configured))
            return configured;

        return DefaultTemplateIds.TryGetValue(group, out var fallback) ? fallback : null;
    }

    public static bool LooksLikeConnectionIdExpiry(int statusCode, string? responseBody) =>
        statusCode is 401 or 403 &&
        !string.IsNullOrWhiteSpace(responseBody) &&
        (responseBody.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
         responseBody.Contains("ConnectionId", StringComparison.OrdinalIgnoreCase));

    public static string FormatConnectionIdExpiryMessage(int statusCode, string? responseBody) =>
        LooksLikeConnectionIdExpiry(statusCode, responseBody)
            ? ConnectionIdExpiredUserMessage
            : TokenExpiredUserMessage;
}

/// <summary>Risk rows from spec §15.</summary>
public enum CrptRiskKind
{
    CryptoProWindowsOnly,
    SuzUrlFromSettingsOnly,
    AttributesPerProductGroup,
    ConnectionIdExpiry,
    TokenTenHourExpiry,
    NkApiRateLimit,
    LargeCatalogPhase2,
    TemplateIdDefaults,
    UotLegalDisclaimer,
}
