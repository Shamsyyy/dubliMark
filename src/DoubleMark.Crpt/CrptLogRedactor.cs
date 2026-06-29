using System.Text.RegularExpressions;

namespace DoubleMark.Crpt;

/// <summary>
/// Redacts marking codes, crypto tails, and auth tokens before logging (spec §12.1).
/// </summary>
public static class CrptLogRedactor
{
    private const char Gs = (char)0x1D;

    private static readonly Regex JwtPattern = new(
        @"eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]*",
        RegexOptions.Compiled);

    private static readonly Regex BearerPattern = new(
        @"Bearer\s+[A-Za-z0-9._\-+/=]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LabeledSecretPattern = new(
        @"(rawPayload|raw_code|rawEscaped|normalizedEscaped|clientToken|access_token|refresh_token|token|nkApiKey|omsId|connectionId|certificateThumbprint)\s*[:=]\s*\S+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Gs1MarkingCodePattern = new(
        @"01\d{14}21[^\s""\\]+",
        RegexOptions.Compiled);

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? string.Empty;

        var redacted = text;
        if (redacted.Contains(Gs, StringComparison.Ordinal))
            redacted = redacted.Replace(Gs, '\uFFFD');

        redacted = JwtPattern.Replace(redacted, "[jwt-redacted]");
        redacted = BearerPattern.Replace(redacted, "Bearer [redacted]");
        redacted = LabeledSecretPattern.Replace(redacted, "$1=[redacted]");
        redacted = Gs1MarkingCodePattern.Replace(redacted, "01**************21[redacted]");

        if (ContainsMarkingCodeIndicators(redacted))
            return "CRPT payload or token content redacted.";

        return redacted.Length <= 512 ? redacted : redacted[..512] + "…";
    }

    /// <summary>
    /// Redacts upstream API error bodies that may echo submitted marking codes.
    /// </summary>
    public static string RedactApiErrorBody(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return body ?? string.Empty;

        if (body.Contains(Gs, StringComparison.Ordinal)
            || Gs1MarkingCodePattern.IsMatch(body)
            || (body.Contains("01", StringComparison.Ordinal)
                && body.Contains("21", StringComparison.Ordinal)
                && body.Contains("92", StringComparison.Ordinal)))
        {
            return "Upstream API rejected the request (response redacted).";
        }

        return Redact(body);
    }

    private static bool ContainsMarkingCodeIndicators(string text) =>
        text.Contains("SYNTHETICPAYLOAD", StringComparison.OrdinalIgnoreCase)
        || (text.Contains("91EE", StringComparison.Ordinal) && text.Contains("92", StringComparison.Ordinal));
}
