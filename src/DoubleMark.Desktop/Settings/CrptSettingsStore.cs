using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DoubleMark.Core.Crpt;
using DoubleMark.Crpt;
using DoubleMark.Desktop.Services;

namespace DoubleMark.Desktop.Settings;

public sealed class CrptSettingsStore : ICrptSettingsStore
{
    public const string SettingsFileName = "crpt-settings.json";
    public const string SecretsFileName = "crpt-secrets.dat";

    private readonly string _settingsPath;
    private readonly string _secretsPath;
    private readonly ICrptSecretsProtector _protector;

    public CrptSettingsStore(
        string? settingsDirectory = null,
        ICrptSecretsProtector? protector = null)
    {
        var directory = settingsDirectory ?? AppSettings.SettingsDirectory;
        _settingsPath = Path.Combine(directory, SettingsFileName);
        _secretsPath = Path.Combine(directory, SecretsFileName);
        _protector = protector ?? new DpapiCrptSecretsProtector();
    }

    public string SettingsPath => _settingsPath;
    public string SecretsPath => _secretsPath;

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public CrptSettings LoadSettings()
    {
        if (!File.Exists(_settingsPath))
            return new CrptSettings();

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<CrptSettings>(json, JsonOptions) ?? new CrptSettings();
            if (TryApplyLoadTimeFixes(settings))
            {
                try
                {
                    var secrets = LoadSecrets();
                    Save(settings, secrets);
                }
                catch (Exception persistEx)
                {
                    LoggingService.Error("CrptSettings", "Persist load-time migration failed", persistEx);
                }
            }

            return settings;
        }
        catch (Exception ex)
        {
            LoggingService.Error("CrptSettings", "LoadSettings failed", ex);
            return new CrptSettings();
        }
    }

    public CrptSecrets LoadSecrets()
    {
        if (!File.Exists(_secretsPath))
            return new CrptSecrets();

        try
        {
            var protectedBytes = File.ReadAllBytes(_secretsPath);
            var jsonBytes = _protector.Unprotect(protectedBytes);
            return JsonSerializer.Deserialize<CrptSecrets>(jsonBytes, JsonOptions) ?? new CrptSecrets();
        }
        catch (Exception ex)
        {
            LoggingService.Error("CrptSettings", "LoadSecrets failed", ex);
            return new CrptSecrets();
        }
    }

    public (CrptSettings Settings, CrptSecrets Secrets) Load()
    {
        return (LoadSettings(), LoadSecrets());
    }

    public CrptSettingsSnapshot LoadMerged() =>
        CrptSettingsSnapshot.Merge(LoadSettings(), LoadSecrets());

    public void Save(CrptSettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var (settings, secrets) = snapshot.Split();
        Save(settings, secrets);
    }

    public void Save(CrptSettings settings, CrptSecrets secrets)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        var settingsJson = JsonSerializer.Serialize(settings, JsonOptions);
        CrptSecurityGuard.ValidateSettingsSnapshot(settings, secrets, settingsJson);
        File.WriteAllText(_settingsPath, settingsJson);

        var secretsBytes = JsonSerializer.SerializeToUtf8Bytes(secrets, JsonOptions);
        var protectedBytes = _protector.Protect(secretsBytes);
        File.WriteAllBytes(_secretsPath, protectedBytes);
    }

    internal static void MigrateLegacyNkBaseUrl(CrptSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.NkBaseUrl))
            return;

        settings.NkBaseUrl = CrptUrl.NormalizeBaseUrl(settings.NkBaseUrl);
    }

    internal static bool TryApplyLoadTimeFixes(CrptSettings settings)
    {
        var changed = false;
        var hadLegacyNk = HasLegacyNkUrl(settings.NkBaseUrl);

        var nkBefore = settings.NkBaseUrl;
        MigrateLegacyNkBaseUrl(settings);
        if (!string.Equals(nkBefore, settings.NkBaseUrl, StringComparison.Ordinal))
            changed = true;

        if (TryReconcileSandboxContourUrls(settings, hadLegacyNk))
            changed = true;

        return changed;
    }

    private static bool TryReconcileSandboxContourUrls(CrptSettings settings, bool hadLegacyNk)
    {
        if (settings.Environment != CrptEnvironment.Sandbox || !hadLegacyNk)
            return false;

        CrptEnvironmentDefaults.Apply(settings, CrptEnvironment.Sandbox);
        return true;
    }

    private static bool HasLegacyNkUrl(string? nkBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(nkBaseUrl))
            return false;

        if (nkBaseUrl.Contains("национальный", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var legacy in CrptUrl.LegacyNkPunycodeHosts)
        {
            if (nkBaseUrl.Contains(legacy, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
