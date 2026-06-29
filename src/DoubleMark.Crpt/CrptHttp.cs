using System.Net.Sockets;

namespace DoubleMark.Crpt;

internal static class CrptHttp
{
    public static string NormalizeBaseUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("Base URL must not be empty.", nameof(url));

        var trimmed = url.Trim();
        trimmed = trimmed.EndsWith('/') ? trimmed : trimmed + "/";
        return NormalizeIdnHost(trimmed);
    }

    public static Uri CreateBaseAddress(string url) => new Uri(NormalizeBaseUrl(url));

    public static string NormalizeIdnHost(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ArgumentException($"Invalid URL: {url}", nameof(url));

        var legacyRemap = RemapLegacyNkHost(uri.Host);
        if (legacyRemap != null)
            return new UriBuilder(uri) { Host = legacyRemap }.Uri.ToString();

        if (uri.Host.All(static c => c < 128))
            return uri.ToString();

        var asciiHost = uri.IdnHost;
        var remapped = RemapLegacyNkHost(asciiHost) ?? asciiHost;
        if (remapped.Equals(uri.Host, StringComparison.OrdinalIgnoreCase))
            return uri.ToString();

        return new UriBuilder(uri) { Host = remapped }.Uri.ToString();
    }

    /// <summary>
    /// Remaps wrong NK hosts to official production punycode (<c>апи.национальный-каталог.рф</c>).
    /// Latin <c>api.</c> subdomain is a different host (often firewalled); official docs use Cyrillic <c>апи.</c>.
    /// </summary>
    private static string? RemapLegacyNkHost(string host)
    {
        if (host.Equals(CrptUrl.ProductionNkPunycodeHost, StringComparison.OrdinalIgnoreCase))
            return null;

        foreach (var legacy in CrptUrl.LegacyNkPunycodeHosts)
        {
            if (host.Equals(legacy, StringComparison.OrdinalIgnoreCase))
                return CrptUrl.ProductionNkPunycodeHost;
        }

        return null;
    }

    public static async Task<string> ReadSuccessBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            response.Headers.TryGetValues(CrptRiskMitigations.ApiUsageLimitHeaderName, out var usageValues);
            throw CrptApiException.FromHttpResponse(
                response.StatusCode,
                body,
                usageValues?.FirstOrDefault());
        }

        return body;
    }

    public static InvalidOperationException ToConnectionException(
        Exception ex,
        string baseUrl,
        string? requestPath,
        CancellationToken cancellationToken,
        int timeoutSeconds)
    {
        var host = TryGetHost(baseUrl) ?? baseUrl;
        var target = string.IsNullOrWhiteSpace(requestPath)
            ? baseUrl
            : $"{baseUrl.TrimEnd('/')}{requestPath}";

        if (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
            return new InvalidOperationException(
                BuildTimeoutMessage(host, baseUrl, target, timeoutSeconds),
                ex);

        if (ex is HttpRequestException httpEx)
        {
            if (httpEx.InnerException is SocketException socketEx)
                return new InvalidOperationException(DescribeSocketError(socketEx, host, baseUrl), ex);

            if (httpEx.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase)
                || httpEx.Message.Contains("TLS", StringComparison.OrdinalIgnoreCase)
                || httpEx.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase))
                return new InvalidOperationException(
                    $"Ошибка SSL/TLS при подключении к Национальному каталогу ({host}). Проверьте антивирус, корпоративный прокси и URL контура (NkBaseUrl). ({baseUrl})",
                    ex);
        }

        return new InvalidOperationException(
            $"Не удалось подключиться к Национальному каталогу ({host}). Проверьте интернет, DNS, VPN и URL контура (NkBaseUrl). ({baseUrl})",
            ex);
    }

    private static string DescribeSocketError(SocketException socketEx, string host, string baseUrl)
    {
        switch (socketEx.SocketErrorCode)
        {
            case SocketError.HostNotFound:
            case SocketError.NoData:
                return
                    $"DNS не находит хост Национального каталога ({host}). Production: {CrptUrl.ProductionNkCyrillicBaseUrl} (punycode {CrptUrl.ProductionNkPunycodeHost}). Sandbox: {CrptUrl.SandboxNkBaseUrl} ({baseUrl})";
            case SocketError.TimedOut:
                return BuildFirewallOrTimeoutMessage(host, baseUrl);
            case SocketError.ConnectionRefused:
                return
                    $"Сервер Национального каталога ({host}) отклонил подключение. Проверьте URL контура (NkBaseUrl). Sandbox: {CrptUrl.SandboxNkBaseUrl} ({baseUrl})";
            default:
                return
                    $"Сетевая ошибка при подключении к Национальному каталогу ({host}): {socketEx.SocketErrorCode}. Проверьте интернет и URL контура (NkBaseUrl). ({baseUrl})";
        }
    }

    internal static bool IsTransientConnectionFailure(Exception ex, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return false;

        if (ex is TaskCanceledException)
            return true;

        if (ex is not HttpRequestException httpEx)
            return false;

        if (httpEx.InnerException is SocketException { SocketErrorCode: SocketError.TimedOut })
            return true;

        return httpEx.InnerException is SocketException;
    }

    private static string BuildTimeoutMessage(string host, string baseUrl, string target, int timeoutSeconds)
    {
        var core =
            $"Таймаут подключения к Национальному каталогу ({host}). Таймаут: {timeoutSeconds}s. ({target})";
        return CrptUrl.IsProductionNkHost(host)
            ? core + " " + ProductionNkFirewallHint
            : core + " Проверьте интернет, VPN и URL контура (NkBaseUrl).";
    }

    private static string BuildFirewallOrTimeoutMessage(string host, string baseUrl) =>
        CrptUrl.IsProductionNkHost(host)
            ? $"Порт 443 к production NK ({host}) недоступен с этой сети (DNS может работать, HTTPS — нет; вероятна блокировка файрволом). {ProductionNkFirewallHint} ({baseUrl})"
            : $"Таймаут подключения к Национальному каталогу ({host}). Проверьте интернет, VPN и URL контура (NkBaseUrl). ({baseUrl})";

    private const string ProductionNkFirewallHint =
        "Попробуйте VPN/корпоративный доступ CRPT или переключите контур на sandbox (NkBaseUrl: " +
        CrptUrl.SandboxNkBaseUrl +
        "). Для sandbox нужны sandbox-ключи/JWT; production УКЭП на sandbox не подходит.";

    private static string? TryGetHost(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null;
}

/// <summary>Public URL helpers for CRPT base addresses (IDN → punycode normalization).</summary>
public static class CrptUrl
{
    /// <summary>
    /// Production NK punycode for official <c>апи.национальный-каталог.рф</c> (API НК docs.crpt.ru; verified Jun 2026).
    /// </summary>
    public const string ProductionNkPunycodeHost = "xn--80aqu.xn----7sbabas4ajkhfocclk9d3cvfsa.xn--p1ai";

    /// <summary>Official Cyrillic production NK URL (normalized to <see cref="ProductionNkPunycodeHost"/>).</summary>
    public const string ProductionNkCyrillicBaseUrl = "https://апи.национальный-каталог.рф/";

    /// <summary>Latin <c>api.</c> subdomain — wrong host (23.111.102.50, often blocked); remapped at runtime.</summary>
    public const string LegacyLatinApiNkPunycodeHost = "api.xn----7sbabas4ajkhfocclk9d3cvfsa.xn--p1ai";

    /// <summary>Obsolete wrong punycode that does not resolve in DNS.</summary>
    public const string LegacyWrongNkPunycodeHost = "api.xn--80ajghhoc2aj1c8b.xn--p1ai";

    /// <summary>All NK hosts remapped to <see cref="ProductionNkPunycodeHost"/> on load.</summary>
    public static readonly string[] LegacyNkPunycodeHosts =
        [LegacyLatinApiNkPunycodeHost, LegacyWrongNkPunycodeHost];

    /// <summary>Production NK API base URL (official punycode host).</summary>
    public const string ProductionNkBaseUrl = "https://" + ProductionNkPunycodeHost + "/";

    /// <summary>Sandbox NK API base URL (reachable when production NK is firewalled).</summary>
    public const string SandboxNkBaseUrl = "https://api.nk.sandbox.crptech.ru/";

    public static string NormalizeBaseUrl(string url) => CrptHttp.NormalizeBaseUrl(url);

    public static bool IsProductionNkHost(string? host) =>
        !string.IsNullOrWhiteSpace(host) &&
        (host.Equals(ProductionNkPunycodeHost, StringComparison.OrdinalIgnoreCase) ||
         host.Equals(LegacyLatinApiNkPunycodeHost, StringComparison.OrdinalIgnoreCase) ||
         host.Contains("национальный", StringComparison.OrdinalIgnoreCase));
}
