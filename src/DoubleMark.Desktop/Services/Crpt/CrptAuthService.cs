using DoubleMark.Crpt;
using DoubleMark.Desktop.Settings;

namespace DoubleMark.Desktop.Services.Crpt;

/// <summary>
/// Singleton token cache with refresh lock (spec §8.2).
/// </summary>
public sealed class CrptAuthService : ICrptAuthService
{
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(15);

    private readonly ICrptSettingsStore _settingsStore;
    private readonly ICrptCertificateProvider _certificateProvider;
    private readonly CrptAuthRuntimeState _runtimeState;
    private readonly Func<CrptConnectionSettings, CrptAuthClient> _authClientFactory;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private CrptAuthToken? _cached;

    public CrptAuthService(
        ICrptSettingsStore settingsStore,
        ICrptCertificateProvider certificateProvider,
        CrptAuthRuntimeState runtimeState,
        Func<CrptConnectionSettings, CrptAuthClient>? authClientFactory = null)
    {
        _settingsStore = settingsStore;
        _certificateProvider = certificateProvider;
        _runtimeState = runtimeState;
        _authClientFactory = authClientFactory ?? (connection => new CrptAuthClient(connection));
    }

    public DateTimeOffset? TokenExpiresAt => _cached?.ExpiresAt;

    public async Task<string> GetValidTokenAsync(CancellationToken cancellationToken = default)
    {
        if (IsCachedTokenValid(_cached))
            return _cached!.Value;

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (IsCachedTokenValid(_cached))
                return _cached!.Value;

            _cached = await RefreshTokenCoreAsync(cancellationToken);
            return _cached.Value;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task RefreshTokenAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            _cached = await RefreshTokenCoreAsync(cancellationToken);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<CrptAuthToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
    {
        var settings = _settingsStore.LoadSettings();
        var secrets = _settingsStore.LoadSecrets();
        var connection = CrptConnectionSettingsBridge.ToConnectionSettings(settings, secrets);
        var certificate = _certificateProvider.FindCertificate(connection);

        using var client = _authClientFactory(connection);
        var token = await client.AuthenticateJwtAsync(certificate, cancellationToken);

        _runtimeState.TokenExpiresAt = token.ExpiresAt;
        return token;
    }

    private static bool IsCachedTokenValid(CrptAuthToken? token) =>
        token is not null
        && CrptAuthResponseParser.IsPlausibleTokenExpiry(token.ExpiresAt)
        && token.ExpiresAt > DateTimeOffset.UtcNow.Add(RefreshSkew);
}
