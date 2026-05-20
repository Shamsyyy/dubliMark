using System.Security.Cryptography;
using System.Text;
using DoubleMark.Core.Models;
using DoubleMark.Core.Parsing;

namespace DoubleMark.Desktop.Services;

public static class ScanDiagnosticsHelper
{
    public static void LogScanReceived(string source, string raw)
    {
        var gsCount = Gs1BarcodeEncoding.CountGs(raw);
        LoggingService.Info("Scanner", $"Scan received source={source} length={raw.Length} gsCount={gsCount}");
        LoggingService.Debug("Scanner", $"escapedPreview={FormatEscapedPreview(raw, 120)}");
        LoggingService.Debug("Scanner", $"hexPreview={Gs1BarcodeEncoding.ToHex(Truncate(raw, 64))}");
    }

    public static void LogParseResult(string source, ParseResult result, string raw)
    {
        var code = result.Code;
        LoggingService.Info("Scanner", $"Parse source={source} valid={result.IsValid} type={code?.CodeType.ToString() ?? "—"}");
        if (!result.IsValid)
        {
            LoggingService.Warn("Scanner", $"Parse failed: {result.ErrorMessage ?? result.ErrorCode?.ToString()}");
            return;
        }

        LoggingService.Info("Scanner",
            $"AI status has01={HasAi(code?.Gtin)} has21={HasAi(code?.Serial)} " +
            $"has91={HasAi(code?.VerificationKey)} has92={HasAi(code?.VerificationCode)} gsCount={Gs1BarcodeEncoding.CountGs(raw)}");

        if (code?.CodeType == MarkingCodeType.Full && !HasFullCrypto(code))
            LoggingService.Warn("Scanner", "Full code missing AI 91/92 — print blocked");

        foreach (var message in result.InfoMessages)
            LoggingService.Warn("Scanner", $"Integrity: {message}");
    }

    public static bool HasFullCrypto(MarkingCode code) =>
        HasAi(code.VerificationKey) && HasAi(code.VerificationCode);

    public static bool IsReadyForPrint(ParseResult result, out string? reason)
    {
        if (!result.IsValid || result.Code == null)
        {
            reason = "код не прошёл проверку";
            return false;
        }

        var code = result.Code;
        if (!HasAi(code.Gtin))
        {
            reason = "отсутствует AI 01 (GTIN)";
            return false;
        }

        if (!HasAi(code.Serial))
        {
            reason = "отсутствует AI 21 (серийный номер)";
            return false;
        }

        if (code.CodeType == MarkingCodeType.Full && !HasFullCrypto(code))
        {
            reason = "отсутствуют AI 91/92";
            return false;
        }

        reason = null;
        return true;
    }

    public static string MaskSerial(string? serial)
    {
        if (string.IsNullOrEmpty(serial))
            return "—";
        if (serial.Length <= 4)
            return "****";
        return serial[..2] + new string('*', serial.Length - 4) + serial[^2..];
    }

    public static string PayloadHash(string raw)
    {
        var bytes = Encoding.UTF8.GetBytes(raw);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash)[..16];
    }

    private static bool HasAi(string? value) => !string.IsNullOrWhiteSpace(value);

    private static string FormatEscapedPreview(string raw, int maxChars)
    {
        return Truncate(raw, maxChars).Replace(Gs1BarcodeEncoding.GsChar.ToString(), "[GS]", StringComparison.Ordinal);
    }

    private static string Truncate(string value, int maxChars) =>
        value.Length <= maxChars ? value : value[..maxChars] + "…";
}
