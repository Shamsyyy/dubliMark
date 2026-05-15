using DubliMark.Core.Models;

namespace DubliMark.Core.Parsing;

public class Gs1Parser
{
    private const char GS = (char)0x1D;

    public ParseResult Parse(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return Fail(ParseErrorCode.Empty, "Пустая строка");

        // Strip AIM identifier prefixes ]d2 / ]C1 / ]Q3
        raw = StripAimIdentifier(raw);

        // Check for GS presence
        if (!raw.Contains(GS))
            return Fail(ParseErrorCode.NoGsSeparator,
                "В коде нет разделителя GS. Проверьте настройку сканера.");

        // AI 01 must be at start
        if (!raw.StartsWith("01"))
            return Fail(ParseErrorCode.NoGtin, "Код не начинается с AI 01");

        if (raw.Length < 16)
            return Fail(ParseErrorCode.InvalidGtinLength, "GTIN короче 14 цифр");

        var gtin = raw.Substring(2, 14);
        if (!gtin.All(char.IsDigit))
            return Fail(ParseErrorCode.InvalidGtinLength, "GTIN содержит не только цифры");

        // Parse Serial
        var rest = raw.Substring(16);
        if (!rest.StartsWith("21"))
            return Fail(ParseErrorCode.NoSerial, "После GTIN ожидается AI 21");

        rest = rest.Substring(2);

        // Serial up to first GS
        var gsIdx = rest.IndexOf(GS);
        if (gsIdx < 0)
            return Fail(ParseErrorCode.NoGsSeparator, "Не найден GS после Serial");

        var serial = rest.Substring(0, gsIdx);
        rest = rest.Substring(gsIdx + 1);

        // Parse optional AI 91 and AI 92
        string? key = null;
        string? code = null;

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

                default:
                    return Fail(ParseErrorCode.UnknownAi, $"Неизвестный AI: {ai}");
            }
        }

        return new ParseResult
        {
            IsValid = true,
            Code = new MarkingCode
            {
                Gtin = gtin,
                Serial = serial,
                VerificationKey = key,
                VerificationCode = code,
                RawData = raw,
                RawDataHex = ToHex(raw)
            }
        };
    }

    private static string StripAimIdentifier(string raw)
    {
        if (raw.StartsWith("]d2") || raw.StartsWith("]C1") || raw.StartsWith("]Q3"))
            return raw.Substring(3);
        return raw;
    }

    private static string ToHex(string s) =>
        string.Join(" ", s.Select(c => ((byte)c).ToString("X2")));

    private static ParseResult Fail(ParseErrorCode code, string msg) =>
        new() { IsValid = false, ErrorCode = code, ErrorMessage = msg };
}
