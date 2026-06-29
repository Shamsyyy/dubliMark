namespace DoubleMark.Desktop.Services.Crpt;

/// <summary>
/// Thread-safe True API token cache and refresh (spec §4.1, §8).
/// </summary>
public interface ICrptAuthService
{
    DateTimeOffset? TokenExpiresAt { get; }

    /// <summary>Returns a valid token, refreshing when missing or within 15 minutes of expiry.</summary>
    Task<string> GetValidTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>Forces a new token exchange regardless of cache (spec §8.4).</summary>
    Task RefreshTokenAsync(CancellationToken cancellationToken = default);
}
