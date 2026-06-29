using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace DoubleMark.Crpt;

public static class CrptIntroduceGoodsBuilder
{
    /// <summary>LP_INTRODUCE_GOODS expects uit_code = short code (01+GTIN+21+serial), before first GS.</summary>
    public static string ToUitCode(string fullMarkingCode)
    {
        const char gs = (char)0x1D;
        var idx = fullMarkingCode.IndexOf(gs);
        return idx >= 0 ? fullMarkingCode[..idx] : fullMarkingCode;
    }

    public static string BuildDocumentJson(
        CrptConnectionSettings settings,
        IReadOnlyList<string> markingCodes,
        DateOnly? productionDate = null)
    {
        var prodDate = productionDate ?? DateOnly.FromDateTime(DateTime.Now);
        var prodDateIso = prodDate.ToString("yyyy-MM-dd") + "T00:00:00.000Z";
        var certDate = settings.CertificateDocDate ?? prodDate.ToString("yyyy-MM-dd") + "T00:00:00.000Z";

        var products = markingCodes.Select(code =>
        {
            var item = new Dictionary<string, object?>
            {
                ["uit_code"] = ToUitCode(code),
                ["uitu_code"] = "",
                ["tnved_code"] = settings.TnvedCode ?? "",
                ["production_date"] = prodDateIso,
                ["producer_inn"] = settings.Inn,
                ["owner_inn"] = settings.Inn
            };

            if (!string.IsNullOrWhiteSpace(settings.CertificateDocNumber))
            {
                item["certificate_document"] = settings.CertificateDocType ?? "CONFORMITY_DECLARATION";
                item["certificate_document_number"] = settings.CertificateDocNumber;
                item["certificate_document_date"] = certDate;
            }

            return item;
        }).ToList();

        var body = new Dictionary<string, object?>
        {
            ["doc_type"] = "Promotion_Inform_Selfmade",
            ["document_description"] = new Dictionary<string, object?>
            {
                ["participant_inn"] = settings.Inn,
                ["producer_inn"] = settings.Inn,
                ["owner_inn"] = settings.Inn,
                ["production_type"] = "OWN_PRODUCTION",
                ["production_date"] = prodDateIso
            },
            ["products"] = products
        };

        return CrptJson.ToCompact(body);
    }
}

public sealed class CrptGisMtClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly HttpClient _suzHttp;
    private readonly CrptConnectionSettings _settings;
    private readonly bool _disposeHttp;
    private readonly bool _disposeSuzHttp;
    private readonly Func<string, X509Certificate2, string> _signOrderBody;

    public CrptGisMtClient(
        CrptConnectionSettings settings,
        HttpClient? httpClient = null,
        HttpClient? suzHttpClient = null,
        Func<string, X509Certificate2, string>? signOrderBody = null)
    {
        _settings = settings;
        _disposeHttp = httpClient is null;
        _disposeSuzHttp = suzHttpClient is null;
        _signOrderBody = signOrderBody ?? CryptoProCadesSigner.SignDetachedOrderBodyBase64;
        _http = httpClient ?? new HttpClient
        {
            BaseAddress = new Uri(CrptHttp.NormalizeBaseUrl(settings.TrueApiBaseUrl))
        };
        _suzHttp = suzHttpClient ?? new HttpClient
        {
            BaseAddress = new Uri(CrptHttp.NormalizeBaseUrl(settings.SuzBaseUrl))
        };
    }

    /// <summary>
    /// Posts utilisation report (spec §10.1, appendix D). Uses united JWT + detached UKEP signature.
    /// </summary>
    public async Task<CrptDocumentSubmitResult> SendUtilisationAsync(
        string jwtToken,
        X509Certificate2 certificate,
        UtilisationReportRequest request,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jwtToken);
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentNullException.ThrowIfNull(request);

        var body = CrptUtilisationBuilder.BuildBody(request);
        var compactJson = CrptJson.ToCompact(body);
        var signature = _signOrderBody(compactJson, certificate);

        var url = $"api/v3/utilisation?omsId={Uri.EscapeDataString(_settings.OmsId)}";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(compactJson, Encoding.UTF8, "application/json"),
        };
        httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwtToken);
        httpRequest.Headers.Add("X-Signature", signature);
        if (!string.IsNullOrWhiteSpace(_settings.OmsId))
            httpRequest.Headers.Add("X-OMS-Id", _settings.OmsId);

        using var response = await _suzHttp.SendAsync(httpRequest, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new CrptGisMtException(
                $"Utilisation report failed ({(int)response.StatusCode}): {CrptLogRedactor.RedactApiErrorBody(text)}");

        var docId = TryReadUtilisationDocumentId(text) ?? Guid.NewGuid().ToString();
        return new CrptDocumentSubmitResult(docId, text);
    }

    public async Task<CrptDocumentSubmitResult> IntroduceGoodsAsync(
        string jwtToken,
        X509Certificate2 certificate,
        string documentJson,
        CancellationToken ct)
    {
        var productDocument = Convert.ToBase64String(Encoding.UTF8.GetBytes(documentJson));
        var signature = CryptoProCadesSigner.SignDetachedOrderBodyBase64(documentJson, certificate);

        var envelope = new Dictionary<string, object?>
        {
            ["document_format"] = "MANUAL",
            ["type"] = "LP_INTRODUCE_GOODS",
            ["product_document"] = productDocument,
            ["signature"] = signature
        };

        var url = $"api/v3/true-api/lk/documents/create?pg={Uri.EscapeDataString(_settings.ProductGroup)}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(envelope, options: CrptJson.Api)
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwtToken);

        using var response = await _http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Introduce goods failed ({(int)response.StatusCode}): {CrptLogRedactor.RedactApiErrorBody(text)}");

        var docId = TryReadDocumentId(text) ?? text.Trim('"');
        return new CrptDocumentSubmitResult(docId, text);
    }

    public async Task<string> GetDocumentInfoAsync(string jwtToken, string documentId, CancellationToken ct)
    {
        var paths = new[]
        {
            $"api/v4/true-api/doc/{Uri.EscapeDataString(documentId)}/info",
            $"api/v3/true-api/lk/doc/{Uri.EscapeDataString(documentId)}/info"
        };

        string? lastError = null;
        foreach (var url in paths)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwtToken);
            using var response = await _http.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode)
                return text;

            lastError = $"HTTP {(int)response.StatusCode}: {CrptLogRedactor.RedactApiErrorBody(text)}";
            if ((int)response.StatusCode is not 404 and not 410)
                break;
        }

        throw new InvalidOperationException(lastError ?? "Document info unavailable");
    }

    private static string? TryReadUtilisationDocumentId(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            foreach (var name in new[] { "reportId", "id", "docId", "doc_id" })
            {
                if (doc.RootElement.TryGetProperty(name, out var value))
                    return value.GetString();
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static string? TryReadDocumentId(string text)
    {
        text = text.Trim();
        if (Guid.TryParse(text.Trim('"'), out _))
            return text.Trim('"');

        try
        {
            using var doc = JsonDocument.Parse(text);
            foreach (var name in new[] { "id", "number", "docId", "doc_id" })
            {
                if (doc.RootElement.TryGetProperty(name, out var value))
                    return value.GetString();
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposeHttp)
            _http.Dispose();
        if (_disposeSuzHttp)
            _suzHttp.Dispose();
    }
}
