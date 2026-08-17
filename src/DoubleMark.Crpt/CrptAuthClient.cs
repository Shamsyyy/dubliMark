using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DoubleMark.Crpt;

public sealed class CrptAuthClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly CrptConnectionSettings _settings;
    private readonly bool _disposeHttpClient;
    private readonly Func<string, X509Certificate2, string> _signAuthKeyData;

    public CrptAuthClient(
        CrptConnectionSettings settings,
        HttpClient? httpClient = null,
        bool disposeHttpClient = true,
        Func<string, X509Certificate2, string>? signAuthKeyData = null)
    {
        _settings = settings;
        _disposeHttpClient = httpClient is null || disposeHttpClient;
        _http = httpClient ?? new HttpClient
        {
            BaseAddress = new Uri(CrptHttp.NormalizeBaseUrl(settings.TrueApiBaseUrl))
        };
        _signAuthKeyData = signAuthKeyData ?? CryptoProCadesSigner.SignAttachedBase64;
    }

    public async Task<CrptAuthToken> AuthenticateForSuzAsync(X509Certificate2 certificate, CancellationToken ct)
    {
        var key = await GetAuthKeyAsync(ct);
        var signed = CryptoProCadesSigner.SignAttachedBase64(key.Data, certificate);

        var path = $"api/v3/true-api/auth/simpleSignIn/{_settings.ConnectionId}";
        using var response = await _http.PostAsJsonAsync(path, new AuthSignInBody(key.Uuid, signed), ct);
        return await ReadSuzTokenAsync(response, ct);
    }

    /// <summary>JWT for GIS MT / NK (Appendix C: inn + unitedToken:false, attached CAdES).</summary>
    public async Task<CrptAuthToken> AuthenticateJwtAsync(X509Certificate2 certificate, CancellationToken ct)
    {
        var key = await GetAuthKeyAsync(ct);
        var signed = _signAuthKeyData(key.Data, certificate);

        var body = new AuthSignInBodyWithInn(key.Uuid, signed, _settings.Inn, UnitedToken: false);
        using var response = await _http.PostAsJsonAsync("api/v3/true-api/auth/simpleSignIn", body, ct);
        return await ReadJwtTokenAsync(response, ct);
    }

    /// <summary>United UUID token (unitedToken:true, inn must not be sent).</summary>
    public async Task<CrptAuthToken> AuthenticateUnitedTokenAsync(X509Certificate2 certificate, CancellationToken ct)
    {
        var key = await GetAuthKeyAsync(ct);
        var signed = _signAuthKeyData(key.Data, certificate);

        var body = new AuthSignInBodyWithUnited(key.Uuid, signed, UnitedToken: true);
        using var response = await _http.PostAsJsonAsync("api/v3/true-api/auth/simpleSignIn", body, ct);
        return await ReadUnitedTokenAsync(response, ct);
    }

    private async Task<CrptAuthKey> GetAuthKeyAsync(CancellationToken ct)
    {
        var text = await _http.GetStringAsync("api/v3/true-api/auth/key", ct);
        return CrptAuthResponseParser.ParseAuthKey(text);
    }

    private static async Task<CrptAuthToken> ReadSuzTokenAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            if (CrptRiskMitigations.LooksLikeConnectionIdExpiry((int)response.StatusCode, text))
            {
                throw new CrptApiException(
                    CrptRiskMitigations.ConnectionIdExpiredUserMessage,
                    (int)response.StatusCode);
            }

            throw CrptApiException.FromHttpResponse(response.StatusCode, text);
        }

        return CrptAuthResponseParser.ParseSuzToken(text);
    }

    private static async Task<CrptAuthToken> ReadJwtTokenAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw CrptApiException.FromHttpResponse(response.StatusCode, text);

        return CrptAuthResponseParser.ParseJwtToken(text);
    }

    private static async Task<CrptAuthToken> ReadUnitedTokenAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw CrptApiException.FromHttpResponse(response.StatusCode, text);

        return CrptAuthResponseParser.ParseUnitedToken(text);
    }

    public void Dispose()
    {
        if (_disposeHttpClient)
            _http.Dispose();
    }

    private sealed record AuthSignInBody(
        [property: JsonPropertyName("uuid")] string Uuid,
        [property: JsonPropertyName("data")] string Data);

    private sealed record AuthSignInBodyWithInn(
        [property: JsonPropertyName("uuid")] string Uuid,
        [property: JsonPropertyName("data")] string Data,
        [property: JsonPropertyName("inn")] string Inn,
        [property: JsonPropertyName("unitedToken")] bool UnitedToken);

    private sealed record AuthSignInBodyWithUnited(
        [property: JsonPropertyName("uuid")] string Uuid,
        [property: JsonPropertyName("data")] string Data,
        [property: JsonPropertyName("unitedToken")] bool UnitedToken);

}
