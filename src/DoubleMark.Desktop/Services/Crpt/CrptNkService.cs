using DoubleMark.Crpt;
using DoubleMark.Desktop.Settings;

namespace DoubleMark.Desktop.Services.Crpt;

/// <summary>
/// NK API facade wired to <see cref="CrptNkClient"/> (spec §5, §9.5).
/// </summary>
public sealed class CrptNkService : ICrptNkService, IDisposable
{
    private readonly ICrptSettingsStore _settingsStore;

    public CrptNkService(ICrptSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    public CrptNkClient CreateNkClient(string? bearerToken = null)
    {
        var settings = _settingsStore.LoadSettings();
        var secrets = _settingsStore.LoadSecrets();
        var connection = ToConnectionSettings(settings, secrets);
        var apiKey = settings.NkUseJwtFromTrueApi ? null : secrets.NkApiKey;
        var token = settings.NkUseJwtFromTrueApi ? bearerToken : null;
        return new CrptNkClient(connection, token, apiKey);
    }

    public CrptTrueApiProductClient CreateTrueApiProductClient()
    {
        var settings = _settingsStore.LoadSettings();
        var secrets = _settingsStore.LoadSecrets();
        return new CrptTrueApiProductClient(ToConnectionSettings(settings, secrets));
    }

    public void Dispose()
    {
    }

    private static CrptConnectionSettings ToConnectionSettings(CrptSettings settings, CrptSecrets secrets)
    {
        var bridge = CrptConnectionSettingsBridge.ToConnectionSettings(settings, secrets);
        bridge.NkBaseUrl = settings.NkBaseUrl;
        bridge.NkHttpTimeoutSeconds = settings.NkHttpTimeoutSeconds;
        return bridge;
    }
}
