using System.Net;

namespace DoubleMark.Crpt;

/// <summary>
/// CRPT HTTP API failure with status-aware helpers (spec §15 — 401 token, 429 NK rate limit).
/// </summary>
public sealed class CrptApiException : InvalidOperationException
{
    public CrptApiException(
        string message,
        int statusCode,
        string? apiUsageLimit = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ApiUsageLimit = apiUsageLimit;
    }

    public int StatusCode { get; }

    /// <summary>Captured from NK <c>API-Usage-Limit</c> response header when present.</summary>
    public string? ApiUsageLimit { get; }

    public bool IsTokenExpired => StatusCode == (int)HttpStatusCode.Unauthorized;

    public bool IsRateLimited => StatusCode == (int)HttpStatusCode.TooManyRequests;

    public static CrptApiException FromHttpResponse(
        HttpStatusCode statusCode,
        string body,
        string? apiUsageLimit = null)
    {
        var code = (int)statusCode;
        var redacted = CrptLogRedactor.RedactApiErrorBody(body);
        var message = $"HTTP {code}: {redacted}";

        if (code == (int)HttpStatusCode.Unauthorized)
            message = $"{CrptRiskMitigations.TokenExpiredUserMessage} ({message})";

        return new CrptApiException(message, code, apiUsageLimit);
    }
}
