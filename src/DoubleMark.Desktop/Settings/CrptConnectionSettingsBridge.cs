using DoubleMark.Crpt;

namespace DoubleMark.Desktop.Settings;

/// <summary>
/// Runtime auth state (not persisted by <see cref="CrptSettingsStore"/>).
/// Managed by <see cref="DoubleMark.Desktop.Services.Crpt.CrptAuthService"/>.
/// </summary>
public sealed class CrptAuthRuntimeState
{
    public DateTimeOffset? TokenExpiresAt { get; set; }
}

public static class CrptConnectionSettingsBridge
{
    public static CrptConnectionSettings ToConnectionSettings(
        CrptSettings settings,
        CrptSecrets secrets,
        string? productGroup = null,
        int? templateId = null)
    {
        var group = productGroup ?? settings.PrimaryProductGroup;
        return new CrptConnectionSettings
        {
            Inn = settings.Inn,
            TrueApiBaseUrl = settings.TrueApiBaseUrl,
            SuzBaseUrl = CrptRiskMitigations.ResolveSuzBaseUrl(settings.SuzBaseUrl),
            NkBaseUrl = settings.NkBaseUrl,
            NkHttpTimeoutSeconds = settings.NkHttpTimeoutSeconds,
            OmsId = secrets.OmsId ?? "",
            ConnectionId = secrets.ConnectionId ?? "",
            CertificateThumbprint = secrets.CertificateThumbprint,
            ContactPerson = settings.ContactPerson ?? "",
            ProductGroup = group,
            TemplateId = templateId ?? settings.ResolveTemplateId(group),
        };
    }
}
