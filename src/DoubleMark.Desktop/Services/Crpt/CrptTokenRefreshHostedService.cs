using DoubleMark.Desktop.Settings;
using Microsoft.Extensions.Hosting;

namespace DoubleMark.Desktop.Services.Crpt;

/// <summary>
/// Background token refresh loop (spec §8.3).
/// </summary>
public class CrptTokenRefreshHostedService : BackgroundService
{
    private readonly ICrptAuthService _authService;
    private readonly ICrptSettingsStore _settingsStore;

    protected virtual TimeSpan RefreshInterval => TimeSpan.FromHours(8);

    public CrptTokenRefreshHostedService(ICrptAuthService authService, ICrptSettingsStore settingsStore)
    {
        _authService = authService;
        _settingsStore = settingsStore;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_settingsStore.LoadSettings().AutoRefreshToken)
            {
                try
                {
                    await _authService.GetValidTokenAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    LoggingService.Error("CrptAuth", "Token refresh failed", ex);
                }
            }

            try
            {
                await Task.Delay(RefreshInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
