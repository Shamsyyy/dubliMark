using DoubleMark.Core.Models;

namespace DoubleMark.Core.Parsing;

/// <summary>
/// Parses GS1 marking codes (ЧЗ). Supports full codes with AI 91/92 and short codes (~31 bytes)
/// from small DataMatrix (22×22–24×24) without crypto tail. Does not synthesize AI 91/92.
/// </summary>
public class Gs1Parser
{
    private const char GS = (char)0x1D;
    /// <summary>Ожидаемая длина серийного номера (AI 21) по спецификации ЧЗ.</summary>
    public const int ExpectedSerialLength = 13;

    private const int MaxPayloadLength = 4096;

    public ParseResult Parse(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return Fail(ParseErrorCode.Empty, "Пустая строка");

        if (raw.Length > MaxPayloadLength)
            return Fail(ParseErrorCode.TruncatedPayload, $"Payload слишком длинный ({raw.Length} байт, максимум {MaxPayloadLength}).");

        var normalized = Gs1BarcodeEncoding.NormalizeForParse(raw);
        raw = normalized.FoundAi01 ? normalized.Payload : StripAimIdentifier(raw);

        if (!raw.Contains(GS))
        {
            if (TryParseShortWithoutGs(raw, out var shortNoGs))
                return shortNoGs;

            if (LooksLikeTruncatedFullMarkingCode(raw))
            {
                return Fail(ParseErrorCode.NoGsSeparator,
                    "Код обрезан: нет разделителя GS (0x1D) и нет блоков AI 91/92. " +
                    "Сканер в режиме HID не передаёт FNC1/GS. Включите в настройках сканера " +
                    "«GS separator» / «Transmit GS» / «FNC1» или переключите на Virtual COM. " +
                    "Для Netum: меню GS/FNC1 → Enable, Prefix/Suffix → GS on.");
            }

            return Fail(ParseErrorCode.NoGsSeparator,
                "В коде нет разделителя GS. Проверьте настройку сканера (GS separator / FNC1).");
        }

        if (!raw.StartsWith("01"))
            return Fail(ParseErrorCode.NoGtin, "Код не начинается с AI 01");

        if (raw.Length < 16)
            return Fail(ParseErrorCode.InvalidGtinLength, "GTIN короче 14 цифр");

        var gtin = raw.Substring(2, 14);
        if (!gtin.All(char.IsDigit))
            return Fail(ParseErrorCode.InvalidGtinLength, "GTIN содержит не только цифры");

        var rest = raw.Substring(16);
        if (!rest.StartsWith("21"))
            return Fail(ParseErrorCode.NoSerial, "После GTIN ожидается AI 21");

        rest = rest.Substring(2);

        var gsIdx = rest.IndexOf(GS);
        if (gsIdx < 0)
            return Fail(ParseErrorCode.NoGsSeparator, "Не найден GS после Serial");

        var serial = rest.Substring(0, gsIdx);
        rest = rest.Substring(gsIdx + 1);

        string? key = null;
        string? code = null;
        string? field93 = null;

        while (rest.Length > 0)
        {
            if (rest.Length < 2)
                return Fail(ParseErrorCode.UnknownAi, "Неполный AI в конце");

            var ai = rest.Substring(0, 2);
            rest = rest.Substring(2);

            switch (ai)
            {
                case "91":
                    var keyEnd = rest.IndexOf(GS);
                    if (keyEnd < 0) { key = rest; rest = ""; }
                    else { key = rest.Substring(0, keyEnd); rest = rest.Substring(keyEnd + 1); }
                    break;

                case "92":
                    var codeEnd = rest.IndexOf(GS);
                    if (codeEnd < 0) { code = rest; rest = ""; }
                    else { code = rest.Substring(0, codeEnd); rest = rest.Substring(codeEnd + 1); }
                    break;

                case "93":
                    var f93End = rest.IndexOf(GS);
                    if (f93End < 0) { field93 = rest; rest = ""; }
                    else { field93 = rest.Substring(0, f93End); rest = rest.Substring(f93End + 1); }
                    break;

                default:
                    if (key != null || code != null)
                        return Fail(ParseErrorCode.UnknownAi, $"Неизвестный AI: {ai}");

                    var unknownEnd = rest.IndexOf(GS);
                    if (unknownEnd < 0) { rest = ""; }
                    else { rest = rest.Substring(unknownEnd + 1); }
                    break;
            }
        }

        if (key != null && code == null)
        {
            return Fail(ParseErrorCode.TruncatedPayload,
                "Неполный полный код: есть AI 91, но отсутствует AI 92.");
        }

        if (code != null && key == null)
        {
            return Fail(ParseErrorCode.TruncatedPayload,
                "Неполный полный код: есть AI 92, но отсутствует AI 91.");
        }

        var codeType = key != null || code != null
            ? MarkingCodeType.Full
            : MarkingCodeType.Short;

        return Success(raw, gtin, serial, key, code, field93, codeType);
    }

    private static IReadOnlyList<string> BuildInfoMessages(
        string raw,
        string serial,
        MarkingCodeType codeType,
        string? field93)
    {
        return [];
    }

    /// <summary>
    /// Short codes from small matrices sometimes arrive without GS when HID strips FNC1.
    /// Pattern: 01+14+21+serial+93+value (~28–36 bytes total).
    /// </summary>
    private static bool TryParseShortWithoutGs(string raw, out ParseResult result)
    {
        result = null!;
        var s = StripAimIdentifier(raw);
        if (!s.StartsWith("01") || s.Length < 22 || s.Length > 40)
            return false;

        if (s.Length < 18 || s.Substring(16, 2) != "21")
            return false;

        if (s.Contains(GS))
            return false;

        var expectedAi93Idx = 18 + ExpectedSerialLength;
        var ai93Idx = s.Length >= expectedAi93Idx + 2
                      && s.AsSpan(expectedAi93Idx, 2).SequenceEqual("93")
            ? expectedAi93Idx
            : s.IndexOf("93", 18, StringComparison.Ordinal);
        if (ai93Idx < 20)
            return false;

        var tailBefore93 = s.Substring(18, ai93Idx - 18);
        if (tailBefore93.Contains("91", StringComparison.Ordinal)
            || tailBefore93.Contains("92", StringComparison.Ordinal))
            return false;

        var serial = tailBefore93;
        if (serial.Length == 0)
            return false;

        var field93 = s.Substring(ai93Idx + 2);
        if (field93.Length == 0 || field93.Length > 20)
            return false;

        var gtin = s.Substring(2, 14);
        if (!gtin.All(char.IsDigit))
            return false;

        result = Success(s, gtin, serial, null, null, field93, MarkingCodeType.Short);
        return true;
    }

    private static bool LooksLikeTruncatedFullMarkingCode(string raw)
    {
        var s = StripAimIdentifier(raw);
        if (!s.StartsWith("01") || s.Length < 20)
            return false;

        if (s.Length < 18 || s.Substring(16, 2) != "21")
            return false;

        if (s.Contains(GS))
            return false;

        if (Gs1BarcodeEncoding.LooksLikeShortMarkingCode(s))
            return false;

        var tail = s.Substring(18);
        var i91 = tail.IndexOf("91", StringComparison.Ordinal);
        var i92 = tail.IndexOf("92", StringComparison.Ordinal);

        // Complete full code without GS: serial + 91 + key + 92 + crypto
        if (i91 >= 0 && i92 > i91)
            return false;

        // Truncated full: AI 91 started but AI 92 missing, or long payload without short 93 tail
        if (i91 >= 0 && i92 < 0)
            return true;

        return s.Length < 55;
    }

    private static string StripAimIdentifier(string raw)
    {
        if (raw.StartsWith("]d2") || raw.StartsWith("]C1") || raw.StartsWith("]Q3"))
            return raw.Substring(3);
        return raw;
    }

    private static string ToHex(string s) =>
        string.Join(" ", s.Select(c => ((byte)c).ToString("X2")));

    private static ParseResult Success(
        string raw,
        string gtin,
        string serial,
        string? key,
        string? code,
        string? field93,
        MarkingCodeType codeType) =>
        new()
        {
            IsValid = true,
            InfoMessages = BuildInfoMessages(raw, serial, codeType, field93),
            Code = new MarkingCode
            {
                Gtin = gtin,
                Serial = serial,
                VerificationKey = key,
                VerificationCode = code,
                AdditionalField93 = field93,
                CodeType = codeType,
                RawData = raw,
                RawDataHex = ToHex(raw)
            }
        };

    private static ParseResult Fail(ParseErrorCode code, string msg) =>
        new() { IsValid = false, ErrorCode = code, ErrorMessage = msg };
}
