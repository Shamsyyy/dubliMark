using DoubleMark.Crpt;
using DoubleMark.Desktop.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DoubleMark.Desktop.Services.Crpt;

/// <summary>
/// Registers CRPT architecture services for dependency injection (spec §4).
/// UI and orchestration consume interfaces only — not <see cref="CrptAuthClient"/> directly.
/// </summary>
public static class CrptServiceRegistration
{
    public static IServiceCollection AddCrptServices(
        this IServiceCollection services,
        string? settingsDirectory = null)
    {
        services.AddSingleton<ICrptSecretsProtector, DpapiCrptSecretsProtector>();
        services.AddSingleton<ICrptSettingsStore>(sp =>
            new CrptSettingsStore(settingsDirectory, sp.GetRequiredService<ICrptSecretsProtector>()));
        services.AddSingleton<CrptAuthRuntimeState>();
        services.AddSingleton<ICrptCertificateProvider, StoreCrptCertificateProvider>();
        services.AddSingleton<ICrptAuthService, CrptAuthService>();
        services.AddSingleton<ICrptNkService, CrptNkService>();
        services.AddSingleton<ICrptProductCatalogStore>(sp =>
        {
            var settings = sp.GetRequiredService<ICrptSettingsStore>().LoadSettings();
            return new CrptProductCatalogStore(settings);
        });
        services.AddSingleton<ICrptCatalogSyncService, CrptCatalogSyncService>();
        services.AddSingleton<CrptOrderRepository>();
        services.AddSingleton<ICrptSuzService, CrptSuzService>();
        services.AddSingleton<ICrptGisMtService, CrptGisMtService>();
        services.AddSingleton<ICrptPrintService, CrptPrintService>();
        services.AddSingleton<CrptTokenRefreshHostedService>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<CrptTokenRefreshHostedService>());

        return services;
    }
}
