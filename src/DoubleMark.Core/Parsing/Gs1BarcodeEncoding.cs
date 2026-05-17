using System.Text;
using System.Text.RegularExpressions;
using DoubleMark.Core.Models;

namespace DoubleMark.Core.Parsing;

/// <summary>
/// Byte-for-byte GS1 barcode payload handling (Latin-1 / ISO-8859-1).
/// Used by image decode (ZXing RawBytes) and tests — preserves 0x1D (GS).
/// </summary>
public static class Gs1BarcodeEncoding
{
    public const char GsChar = (char)0x1D;
    private const byte GsByte = 0x1D;
    private const byte E8Byte = 0xE8;
    private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

    private static readonly Regex Gs1CzPattern = new(
        @"^01\d{14}21.+91.+92",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex Ai01Pattern = new(
        @"01\d{14}",
        RegexOptions.CultureInvariant);

    /// <summary>Builds a Latin-1 string from raw barcode bytes after GS1 normalization.</summary>
    public static string BytesToRawString(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            return string.Empty;

        var normalized = NormalizeForParse(bytes);
        return Latin1.GetString(normalized.Bytes);
    }

    /// <summary>
    /// Strips ZXing/FNC1 preamble, fixes targeted E8→GS separators, slices from AI 01 + 14-digit GTIN.
    /// </summary>
    public static Gs1NormalizeResult NormalizeForParse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            return new Gs1NormalizeResult
            {
                Payload = string.Empty,
                Bytes = Array.Empty<byte>(),
                FoundAi01 = false,
                Ai01Offset = -1
            };

        var working = NormalizeGsBytes(bytes);
        var ai01Offset = FindAi01ByteOffset(working);
        var stripped = 0;

        if (ai01Offset > 0)
        {
            stripped = ai01Offset;
            working = working.AsSpan(ai01Offset).ToArray();
        }
        else if (ai01Offset < 0)
        {
            var text = Latin1.GetString(working);
            var textOffset = FindAi01TextOffset(text);
            if (textOffset >= 0)
            {
                ai01Offset = textOffset;
                stripped = textOffset;
                working = Latin1.GetBytes(text.AsSpan(textOffset).ToString());
            }
        }

        var payload = Latin1.GetString(working);
        return new Gs1NormalizeResult
        {
            Payload = payload,
            Bytes = working,
            StrippedPrefixBytes = stripped,
            Ai01Offset = ai01Offset,
            FoundAi01 = ai01Offset >= 0 || (working.Length >= 16 && StartsWithAi01(working))
        };
    }

    /// <summary>String overload for scanner text and already-decoded payloads.</summary>
    public static Gs1NormalizeResult NormalizeForParse(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return new Gs1NormalizeResult
            {
                Payload = string.Empty,
                Bytes = Array.Empty<byte>(),
                FoundAi01 = false
            };

        return NormalizeForParse(Latin1.GetBytes(raw));
    }

    /// <summary>True if buffer contains ASCII 01 followed by 14 decimal digits.</summary>
    public static bool ContainsAi01Pattern(ReadOnlySpan<byte> bytes) =>
        FindAi01ByteOffset(bytes) >= 0;

    /// <summary>Normalizes FNC1/GS bytes; E8→GS only at known separator positions (not globally).</summary>
    public static byte[] NormalizeGsBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            return Array.Empty<byte>();

        var copy = bytes.ToArray();
        ReplaceKnownE8Separators(copy);
        return copy;
    }

    private static void ReplaceKnownE8Separators(Span<byte> copy)
    {
        for (var i = 0; i < copy.Length - 2; i++)
        {
            if (copy[i] != E8Byte)
                continue;

            // GS1 field separator before AI 91, 92, or 93 (short marking code)
            if (copy[i + 1] == (byte)'9' && (copy[i + 2] is (byte)'1' or (byte)'2' or (byte)'3'))
                copy[i] = GsByte;
        }
    }

    private static int FindAi01ByteOffset(ReadOnlySpan<byte> bytes)
    {
        var fallback = -1;
        for (var i = 0; i <= bytes.Length - 16; i++)
        {
            if (!StartsWithAi01At(bytes, i))
                continue;

            if (i + 18 <= bytes.Length && bytes[i + 16] == (byte)'2' && bytes[i + 17] == (byte)'1')
                return i;

            fallback = i;
        }

        return fallback;
    }

    private static int FindAi01TextOffset(string text)
    {
        var match = Ai01Pattern.Match(text);
        return match.Success ? match.Index : -1;
    }

    private static bool StartsWithAi01(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 16 && StartsWithAi01At(bytes, 0);

    private static bool StartsWithAi01At(ReadOnlySpan<byte> bytes, int offset)
    {
        if (offset + 16 > bytes.Length)
            return false;

        if (bytes[offset] != (byte)'0' || bytes[offset + 1] != (byte)'1')
            return false;

        for (var j = 2; j < 16; j++)
        {
            var b = bytes[offset + j];
            if (b < (byte)'0' || b > (byte)'9')
                return false;
        }

        return true;
    }

    public static int CountGs(string raw) =>
        raw.Count(c => c == GsChar);

    /// <summary>
    /// Canonical GS1 payload for DataMatrix encoding. Re-inserts GS when HID/COM delivered
    /// a concatenated string without FNC1/GS separators.
    /// </summary>
    public static string BuildBarcodePayload(MarkingCode code)
    {
        ArgumentNullException.ThrowIfNull(code);

        if (CountGs(code.RawData) > 0)
            return code.RawData;

        var gs = GsChar.ToString();
        var sb = new StringBuilder();
        sb.Append("01").Append(code.Gtin).Append("21").Append(code.Serial);

        if (code.VerificationKey != null || code.VerificationCode != null)
        {
            if (code.VerificationKey != null)
                sb.Append(gs).Append("91").Append(code.VerificationKey);
            if (code.VerificationCode != null)
                sb.Append(gs).Append("92").Append(code.VerificationCode);
        }
        else if (code.AdditionalField93 != null)
            sb.Append(gs).Append("93").Append(code.AdditionalField93);
        else if (code.CodeType == MarkingCodeType.Short)
            sb.Append(gs);

        return sb.ToString();
    }

    public static string ToHex(string raw) =>
        string.Join(" ", raw.Select(c => ((byte)c).ToString("X2")));

    public static string ToHex(ReadOnlySpan<byte> bytes) =>
        string.Join(" ", bytes.ToArray().Select(b => b.ToString("X2")));

    /// <summary>Higher score = more likely a complete ЧЗ GS1 DataMatrix payload.</summary>
    public static int ScoreGs1Payload(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return 0;

        var norm = NormalizeForParse(raw);
        var payload = norm.FoundAi01 ? norm.Payload : raw;
        var byteLen = norm.Bytes.Length > 0 ? norm.Bytes.Length : Latin1.GetByteCount(payload);

        var score = 0;
        if (norm.FoundAi01)
            score += 80;

        var gs = CountGs(payload);
        score += gs * 40;

        if (gs >= 2)
            score += 35;

        if (payload.Contains(GsChar))
            score += 30;

        if (Gs1CzPattern.IsMatch(payload))
            score += 50;

        if (payload.Length >= 16 && payload.StartsWith("01", StringComparison.Ordinal))
        {
            score += 10;
            if (payload.Substring(2, 14).All(char.IsDigit))
                score += 15;
        }

        if (payload.Length >= 18 && payload.AsSpan(16, 2).SequenceEqual("21"))
            score += 10;

        if (byteLen > 40)
            score += 30;
        if (byteLen > 80)
            score += 40;

        var firstGs = payload.IndexOf(GsChar);
        if (firstGs >= 0)
        {
            var afterFirstGs = payload.AsSpan(firstGs + 1);
            if (afterFirstGs.Contains("91", StringComparison.Ordinal))
                score += 25;
            if (afterFirstGs.Contains("92", StringComparison.Ordinal))
                score += 25;
        }

        if (payload.Contains("91", StringComparison.Ordinal) && payload.Contains("92", StringComparison.Ordinal))
            score += 20;

        if (LooksLikeShortMarkingCode(payload))
            score += 45;

        return score;
    }

    /// <summary>
    /// Короткий код маркировки: 01+GTIN+21+serial[+GS+93+value], без AI 91/92.
    /// Типично ~28–36 байт (DataMatrix 22×22–24×24).
    /// </summary>
    public static bool LooksLikeShortMarkingCode(string payload)
    {
        if (payload.Length < 22 || payload.Length > 40 || !payload.StartsWith("01", StringComparison.Ordinal))
            return false;

        if (payload.Length < 18 || !payload.AsSpan(16, 2).SequenceEqual("21"))
            return false;

        if (payload.Contains($"{GsChar}91", StringComparison.Ordinal)
            || payload.Contains($"{GsChar}92", StringComparison.Ordinal))
            return false;

        var gsIdx = payload.IndexOf(GsChar, 18);
        if (gsIdx >= 0)
        {
            var afterGs = payload.AsSpan(gsIdx + 1);
            if (afterGs.StartsWith("93", StringComparison.Ordinal))
                return true;

            return afterGs.IsEmpty;
        }

        return payload.AsSpan(18).Contains("93", StringComparison.Ordinal);
    }

    /// <summary>Obsolete name — short codes with AI 93 are valid, not truncated.</summary>
    public static bool LooksLikeTruncatedAi93(string payload) => false;

    public static bool LooksLikeGs1Cz(string raw) => ScoreGs1Payload(raw) >= 50;
}
