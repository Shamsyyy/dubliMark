using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DoubleMark.Api.Options;
using Microsoft.Extensions.Options;

namespace DoubleMark.Api.Billing;

public sealed record AlphaRegisterResult(
    bool Success,
    string? OrderId,
    string? FormUrl,
    string? Error);

public sealed class AlphaBankClient
{
    private readonly HttpClient _http;
    private readonly AlphaBankOptions _options;
    private readonly ILogger<AlphaBankClient> _logger;

    public AlphaBankClient(
        HttpClient http,
        IOptions<AlphaBankOptions> options,
        ILogger<AlphaBankClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsLive => _options.IsConfigured;

    public async Task<AlphaRegisterResult> RegisterOrderAsync(
        Guid paymentId,
        int amountKopecks,
        string description,
        string returnUrl,
        CancellationToken ct)
    {
        if (!_options.IsConfigured)
        {
            return new AlphaRegisterResult(
                true,
                paymentId.ToString("N"),
                null,
                null);
        }

        var baseUrl = _options.ApiBaseUrl.TrimEnd('/') + "/";
        var form = new Dictionary<string, string>
        {
            ["userName"] = _options.UserName,
            ["password"] = _options.Password,
            ["orderNumber"] = paymentId.ToString("N"),
            ["amount"] = amountKopecks.ToString(),
            ["currency"] = "643",
            ["returnUrl"] = returnUrl,
            ["description"] = description
        };

        using var content = new FormUrlEncodedContent(form);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

        try
        {
            using var response = await _http.PostAsync(baseUrl + "register.do", content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            var root = doc.RootElement;

            var errorCode = root.TryGetProperty("errorCode", out var ec) ? ec.ToString() : "0";
            if (errorCode is not ("0" or ""))
            {
                var msg = root.TryGetProperty("errorMessage", out var em)
                    ? em.GetString()
                    : "Ошибка регистрации платежа в банке.";
                _logger.LogWarning("Alpha register failed: {Code} {Message}", errorCode, msg);
                return new AlphaRegisterResult(false, null, null, msg);
            }

            var orderId = root.TryGetProperty("orderId", out var oid) ? oid.GetString() : null;
            var formUrl = root.TryGetProperty("formUrl", out var fu) ? fu.GetString() : null;
            if (string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(formUrl))
                return new AlphaRegisterResult(false, null, null, "Банк не вернул formUrl/orderId.");

            return new AlphaRegisterResult(true, orderId, formUrl, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Alpha register.do failed");
            return new AlphaRegisterResult(false, null, null, "Не удалось связаться с платёжным шлюзом.");
        }
    }

    public bool VerifyWebhookSignature(string? provided, string payload)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookSecret))
            return true;
        if (string.IsNullOrWhiteSpace(provided))
            return false;

        using var hmac = new System.Security.Cryptography.HMACSHA256(
            Encoding.UTF8.GetBytes(_options.WebhookSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var expected = Convert.ToHexString(hash).ToLowerInvariant();
        return string.Equals(expected, provided.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
