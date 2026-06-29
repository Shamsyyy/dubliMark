using System.Net.Http.Headers;
using System.Text;

namespace DoubleMark.Crpt;

/// <summary>
/// National catalog (NK) HTTP client skeleton (spec §5, §9.5.2).
/// Full sync orchestration arrives in later sections.
/// </summary>
public sealed class CrptNkClient : IDisposable
{
    /// <summary>Placeholder backoff for NK 429 rate limits (spec §15).</summary>
    public static TimeSpan RateLimitBackoffPlaceholder => CrptRiskMitigations.NkRateLimitInitialBackoff;

    private readonly HttpClient _http;
    private readonly CrptConnectionSettings _settings;
    private readonly string? _bearerToken;
    private readonly string? _apiKey;
    private readonly string _normalizedBaseUrl;
    private readonly bool _ownsHttpClient;

    /// <summary>Last captured <see cref="CrptRiskMitigations.ApiUsageLimitHeaderName"/> value, if any.</summary>
    public string? LastApiUsageLimit { get; private set; }

    public CrptNkClient(
        CrptConnectionSettings settings,
        string? bearerToken = null,
        string? apiKey = null,
        HttpClient? httpClient = null)
    {
        _settings = settings;
        _bearerToken = bearerToken;
        _apiKey = apiKey;
        _normalizedBaseUrl = CrptHttp.NormalizeBaseUrl(settings.NkBaseUrl);

        var timeoutSeconds = settings.NkHttpTimeoutSeconds > 0
            ? settings.NkHttpTimeoutSeconds
            : CrptRiskMitigations.NkHttpTimeoutSeconds;

        if (httpClient is null)
        {
            _http = CrptNkHttpFactory.CreateClient(settings.NkBaseUrl, timeoutSeconds);
            _ownsHttpClient = true;
        }
        else
        {
            _http = httpClient;
            _ownsHttpClient = false;
        }
    }

    public async Task<string> GetProductListAsync(
        int limit = 1000,
        int offset = 0,
        string? goodStatus = null,
        string? fromDate = null,
        string? toDate = null,
        CancellationToken ct = default)
    {
        var query = new StringBuilder($"v4/product-list?limit={limit}&offset={offset}");
        if (!string.IsNullOrWhiteSpace(goodStatus))
            query.Append("&good_status=").Append(Uri.EscapeDataString(goodStatus));
        if (!string.IsNullOrWhiteSpace(fromDate))
            query.Append("&from_date=").Append(Uri.EscapeDataString(fromDate));
        if (!string.IsNullOrWhiteSpace(toDate))
            query.Append("&to_date=").Append(Uri.EscapeDataString(toDate));
        AppendAuthQuery(query);

        using var response = await SendNkRequestAsync(
            () => CreateGetRequest(query.ToString()),
            ct);
        return await ReadNkResponseAsync(response, ct);
    }

    public async Task<string> GetFeedProductAsync(IReadOnlyList<string> gtins, CancellationToken ct = default)
    {
        if (gtins.Count == 0)
            throw new ArgumentException("At least one GTIN is required.", nameof(gtins));
        if (gtins.Count > 25)
            throw new ArgumentException("NK feed-product accepts at most 25 GTINs per request.", nameof(gtins));

        var joined = string.Join(';', gtins);
        var query = new StringBuilder($"v3/feed-product?gtins={Uri.EscapeDataString(joined)}");
        AppendAuthQuery(query);

        using var response = await SendNkRequestAsync(
            () => CreateGetRequest(query.ToString()),
            ct);
        return await ReadNkResponseAsync(response, ct);
    }

    public async Task<string> GetFeedProductByGoodIdAsync(int goodId, CancellationToken ct = default)
    {
        if (goodId <= 0)
            throw new ArgumentOutOfRangeException(nameof(goodId));

        var query = new StringBuilder($"v3/feed-product?good_id={goodId}");
        AppendAuthQuery(query);

        using var response = await SendNkRequestAsync(
            () => CreateGetRequest(query.ToString()),
            ct);
        return await ReadNkResponseAsync(response, ct);
    }

    public async Task<string> GetEtagsListAsync(int offset = 0, CancellationToken ct = default)
    {
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));

        var query = new StringBuilder($"v3/etagslist?offset={offset}");
        AppendAuthQuery(query);

        using var response = await SendNkRequestAsync(
            () => CreateGetRequest(query.ToString()),
            ct);
        return await ReadNkResponseAsync(response, ct);
    }

    private HttpRequestMessage CreateGetRequest(string relativeUri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, relativeUri);
        ApplyAuthHeaders(request);
        return request;
    }

    private async Task<HttpResponseMessage> SendNkRequestAsync(
        Func<HttpRequestMessage> createRequest,
        CancellationToken ct)
    {
        var delay = CrptRiskMitigations.NkConnectionRetryInitialBackoff;
        var maxAttempts = CrptRiskMitigations.NkConnectionRetryAttempts;
        Exception? lastFailure = null;
        string? requestPath = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var request = createRequest();
            requestPath ??= GetRequestPath(request.RequestUri);

            try
            {
                return await _http.SendAsync(request, ct);
            }
            catch (Exception ex) when (attempt < maxAttempts && CrptHttp.IsTransientConnectionFailure(ex, ct))
            {
                lastFailure = ex;
                await Task.Delay(delay, ct);
                delay += delay;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw ToConnectionException(ex, requestPath, ct);
            }
        }

        throw ToConnectionException(lastFailure!, requestPath, ct);
    }

    private InvalidOperationException ToConnectionException(
        Exception ex,
        string? requestPath,
        CancellationToken cancellationToken)
    {
        var timeoutSeconds = _settings.NkHttpTimeoutSeconds > 0
            ? _settings.NkHttpTimeoutSeconds
            : CrptRiskMitigations.NkHttpTimeoutSeconds;

        return CrptHttp.ToConnectionException(
            ex,
            _normalizedBaseUrl,
            requestPath,
            cancellationToken,
            timeoutSeconds);
    }

    private async Task<string> ReadNkResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        CaptureApiUsageLimit(response);
        try
        {
            return await CrptHttp.ReadSuccessBodyAsync(response, ct);
        }
        catch (CrptApiException ex) when (ex.IsRateLimited)
        {
            throw new CrptApiException(
                $"{ex.Message} NK rate limit; retry after {RateLimitBackoffPlaceholder.TotalSeconds}s.",
                ex.StatusCode,
                ex.ApiUsageLimit ?? LastApiUsageLimit,
                ex);
        }
    }

    private void CaptureApiUsageLimit(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues(CrptRiskMitigations.ApiUsageLimitHeaderName, out var values))
            LastApiUsageLimit = values.FirstOrDefault();
    }

    private void ApplyAuthHeaders(HttpRequestMessage request)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(_bearerToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);
    }

    private void AppendAuthQuery(StringBuilder query)
    {
        if (string.IsNullOrWhiteSpace(_bearerToken) && !string.IsNullOrWhiteSpace(_apiKey))
            query.Append("&apikey=").Append(Uri.EscapeDataString(_apiKey));
    }

    private static string? GetRequestPath(Uri? requestUri)
    {
        if (requestUri is null)
            return null;

        return requestUri.IsAbsoluteUri
            ? requestUri.PathAndQuery
            : requestUri.OriginalString;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _http.Dispose();
    }
}
