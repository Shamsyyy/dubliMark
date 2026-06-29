using DoubleMark.Core.Crpt;

namespace DoubleMark.Desktop.Settings;

/// <summary>
/// Complete §6.1 settings view: non-secret <see cref="CrptSettings"/> plus DPAPI <see cref="CrptSecrets"/>.
/// </summary>
public sealed record CrptSettingsSnapshot(CrptSettings Settings, CrptSecrets Secrets)
{
    public static CrptSettingsSnapshot Merge(CrptSettings settings, CrptSecrets secrets) =>
        new(settings, secrets);

    public (CrptSettings Settings, CrptSecrets Secrets) Split() => (Settings, Secrets);
}
