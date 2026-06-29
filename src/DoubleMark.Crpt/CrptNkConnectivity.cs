using System.Net.Http;

namespace DoubleMark.Crpt;

/// <summary>Fast NK reachability probe before long catalog sync (short connect timeout, no retries).</summary>
public static class CrptNkConnectivity
{
    public const int HealthCheckTimeoutSeconds = 20;
    public const int HealthCheckConnectTimeoutSeconds = 15;

    public static async Task CheckReachableAsync(string nkBaseUrl, CancellationToken cancellationToken = default)
    {
        using var client = CrptNkHttpFactory.CreateClient(
            nkBaseUrl,
            HealthCheckTimeoutSeconds,
            HealthCheckConnectTimeoutSeconds);

        var normalizedBaseUrl = CrptHttp.NormalizeBaseUrl(nkBaseUrl);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "v4/product-list?limit=1&offset=0");
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw CrptHttp.ToConnectionException(
                ex,
                normalizedBaseUrl,
                "/v4/product-list",
                cancellationToken,
                HealthCheckTimeoutSeconds);
        }
    }

    public static async Task<(bool Success, string? ErrorMessage)> TryCheckReachableAsync(
        string nkBaseUrl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await CheckReachableAsync(nkBaseUrl, cancellationToken);
            return (true, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
