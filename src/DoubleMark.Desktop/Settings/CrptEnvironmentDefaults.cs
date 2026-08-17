using DoubleMark.Core.Crpt;
using DoubleMark.Crpt;

namespace DoubleMark.Desktop.Settings;

/// <summary>
/// Default base URLs per CRPT contour (spec §6.1, §9.5.1).
/// </summary>
public static class CrptEnvironmentDefaults
{
    public const string SandboxNkBaseUrl = CrptUrl.SandboxNkBaseUrl;
    public const string ProductionNkBaseUrl = CrptUrl.ProductionNkBaseUrl;
    public const string SandboxSuzBaseUrl = "https://suz.sandbox.crptech.ru/";
    public const string SandboxTrueApiBaseUrl = "https://markirovka.sandbox.crptech.ru/";

    public static void Apply(CrptSettings settings, CrptEnvironment environment)
    {
        settings.Environment = environment;

        if (environment == CrptEnvironment.Sandbox)
        {
            settings.SuzBaseUrl = SandboxSuzBaseUrl;
            settings.TrueApiBaseUrl = SandboxTrueApiBaseUrl;
            settings.NkBaseUrl = SandboxNkBaseUrl;
            return;
        }

        settings.SuzBaseUrl = CrptSettings.DefaultSuzBaseUrl;
        settings.TrueApiBaseUrl = CrptSettings.DefaultTrueApiBaseUrl;
        settings.NkBaseUrl = ProductionNkBaseUrl;
    }
}
