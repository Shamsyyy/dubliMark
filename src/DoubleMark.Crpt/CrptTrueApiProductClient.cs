using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace DoubleMark.Crpt;

/// <summary>
/// True API product/info client skeleton (spec §5, §9.5.2 step 3).
/// </summary>
public sealed class CrptTrueApiProductClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly CrptConnectionSettings _settings;

    public CrptTrueApiProductClient(CrptConnectionSettings settings, HttpClient? httpClient = null)
    {
        _settings = settings;
        _http = httpClient ?? new HttpClient
        {
            BaseAddress = new Uri(CrptHttp.NormalizeBaseUrl(settings.TrueApiBaseUrl)),
        };
    }

    public async Task<string> GetProductInfoAsync(string jwtToken, IReadOnlyList<string> gtins, CancellationToken ct = default)
    {
        if (gtins.Count == 0)
            throw new ArgumentException("At least one GTIN is required.", nameof(gtins));

        var body = new Dictionary<string, object> { ["gtins"] = gtins };
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v4/true-api/product/info")
        {
            Content = JsonContent.Create(body, options: CrptJson.Api),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwtToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _http.SendAsync(request, ct);
        return await CrptHttp.ReadSuccessBodyAsync(response, ct);
    }

    public void Dispose() => _http.Dispose();
}
