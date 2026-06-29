namespace DoubleMark.Desktop.Settings;

/// <summary>
/// Persists CRPT settings and DPAPI-protected secrets (spec §4.1, §7).
/// </summary>
public interface ICrptSettingsStore
{
    string SettingsPath { get; }
    string SecretsPath { get; }

    CrptSettings LoadSettings();
    CrptSecrets LoadSecrets();
    (CrptSettings Settings, CrptSecrets Secrets) Load();
    CrptSettingsSnapshot LoadMerged();
    void Save(CrptSettings settings, CrptSecrets secrets);
    void Save(CrptSettingsSnapshot snapshot);
}
