using System.Text;
using System.Text.RegularExpressions;
using DoubleMark.Core.Models;

namespace DoubleMark.Core.Parsing;

public enum IntegritySeverity
{
    Info,
    Warning,
    Error
}

public enum IntegrityIssueCode
{
    InvalidGtinCheckDigit,
    MissingGsInRaw,
    SubstitutedGsSeparator,
    UnexpectedGsCount,
    TruncatedCryptoTail,
    InvalidAi91Length,
    InvalidAi92Length,
    InvalidAi92Charset,
    SuspiciousSerialLength,
    LowStructureScore,
    SerialContainsAiMarker,
    EmptyPayload
}

public sealed record IntegrityIssue(
    IntegritySeverity Severity,
    IntegrityIssueCode Code,
    string Message);

/// <summary>
/// Second-pass ЧЗ integrity checks after <see cref="Gs1Parser"/> (catches scanner damage and structural defects).
/// </summary>
public static class MarkingCodeIntegrity
{
    private const int FullAi91ExpectedLength = 4;
    private const int FullAi92ExpectedLength = 44;
    private static readonly Regex Base64ish = new(@"^[A-Za-z0-9+/=_\-]+$", RegexOptions.Compiled);

    public static IReadOnlyList<IntegrityIssue> Assess(string raw, ParseResult parse)
    {
        var issues = new List<IntegrityIssue>();

        if (string.IsNullOrEmpty(raw))
        {
            issues.Add(Issue(IntegritySeverity.Error, IntegrityIssueCode.EmptyPayload, "Пустой payload скана."));
            return issues;
        }

        AssessRawTransport(raw, parse, issues);

        if (!parse.IsValid || parse.Code == null)
            return issues;

        AssessGtin(parse.Code.Gtin, issues);
        AssessSerial(parse.Code, issues);
        AssessGsStructure(raw, parse, issues);
        AssessFullCryptoTail(parse.Code, issues);

        var score = Gs1BarcodeEncoding.ScoreGs1Payload(raw);
        if (score < 50)
        {
            issues.Add(Issue(IntegritySeverity.Warning, IntegrityIssueCode.LowStructureScore,
                $"Низкий структурный score payload ({score}). Возможно повреждение или не ЧЗ."));
        }

        return issues;
    }

    public static ParseResult Enrich(ParseResult parse, string raw)
    {
        var issues = Assess(raw, parse);
        if (issues.Count == 0)
            return parse;

        var messages = issues
            .Where(i => i.Severity is IntegritySeverity.Warning or IntegritySeverity.Error)
            .Select(i => i.Message)
            .Distinct()
            .ToArray();

        if (messages.Length == 0)
            return parse;

        return parse with
        {
            InfoMessages = parse.InfoMessages.Concat(messages).ToArray()
        };
    }

    public static bool ShouldTreatAsBroken(ParseResult parse, IReadOnlyList<IntegrityIssue> issues) =>
        !parse.IsValid || issues.Any(i => i.Severity == IntegritySeverity.Error);

    private static void AssessRawTransport(string raw, ParseResult parse, List<IntegrityIssue> issues)
    {
        if (raw.Contains(Gs1BarcodeEncoding.GsChar))
            return;

        if (parse.IsValid && parse.Code?.CodeType == MarkingCodeType.Full)
        {
            issues.Add(Issue(IntegritySeverity.Error, IntegrityIssueCode.MissingGsInRaw,
                "Полный код без GS (0x1D) в сыром скане — сканер мог съесть FNC1."));
        }
        else if (raw.Contains(' ') || raw.Contains('\t'))
        {
            issues.Add(Issue(IntegritySeverity.Warning, IntegrityIssueCode.SubstitutedGsSeparator,
                "В сыром скане есть пробел/Tab вместо GS — проверьте режим HID/COM."));
        }
        else if (!parse.IsValid && parse.ErrorCode == ParseErrorCode.NoGsSeparator)
        {
            issues.Add(Issue(IntegritySeverity.Error, IntegrityIssueCode.MissingGsInRaw,
                "Нет разделителя GS в сыром payload."));
        }
    }

    private static void AssessGtin(string gtin, List<IntegrityIssue> issues)
    {
        if (gtin.Length != 14 || !gtin.All(char.IsDigit))
            return;

        if (!IsValidGtinCheckDigit(gtin))
        {
            issues.Add(Issue(IntegritySeverity.Warning, IntegrityIssueCode.InvalidGtinCheckDigit,
                $"Контрольная цифра GTIN не сходится (GTIN {gtin})."));
        }
    }

    private static void AssessSerial(MarkingCode code, List<IntegrityIssue> issues)
    {
        if (code.Serial.Contains("91", StringComparison.Ordinal)
            || code.Serial.Contains("92", StringComparison.Ordinal))
        {
            issues.Add(Issue(IntegritySeverity.Warning, IntegrityIssueCode.SerialContainsAiMarker,
                "Серийный номер содержит «91»/«92» — возможно смещение AI из-за потери GS."));
        }

        if (code.CodeType == MarkingCodeType.Full
            && code.Serial.Length != Gs1Parser.ExpectedSerialLength)
        {
            issues.Add(Issue(IntegritySeverity.Warning, IntegrityIssueCode.SuspiciousSerialLength,
                $"Длина серийного номера {code.Serial.Length} (ожидается {Gs1Parser.ExpectedSerialLength} для полного кода)."));
        }
    }

    private static void AssessGsStructure(string raw, ParseResult parse, List<IntegrityIssue> issues)
    {
        var code = parse.Code!;
        var gsInRaw = Gs1BarcodeEncoding.CountGs(raw);
        var gsInNormalized = Gs1BarcodeEncoding.CountGs(code.RawData);

        // Короткий код с AI 93 без GS в сыром скане — нормальный HID wedge (как в логах COM vs HID).
        if (code.CodeType == MarkingCodeType.Short
            && code.AdditionalField93 != null
            && gsInRaw == 0
            && gsInNormalized == 0)
        {
            return;
        }

        var expected = code.CodeType switch
        {
            MarkingCodeType.Full => 2,
            MarkingCodeType.Short when code.AdditionalField93 != null => 1,
            _ => 0
        };

        if (expected > 0 && gsInNormalized != expected)
        {
            issues.Add(Issue(IntegritySeverity.Warning, IntegrityIssueCode.UnexpectedGsCount,
                $"Ожидалось GS={expected}, в нормализованном коде GS={gsInNormalized} (в сыром скане GS={gsInRaw})."));
        }
    }

    private static void AssessFullCryptoTail(MarkingCode code, List<IntegrityIssue> issues)
    {
        if (code.CodeType != MarkingCodeType.Full)
            return;

        if (code.VerificationKey == null || code.VerificationCode == null)
        {
            issues.Add(Issue(IntegritySeverity.Error, IntegrityIssueCode.TruncatedCryptoTail,
                "Полный код без пары AI 91/92."));
            return;
        }

        if (code.VerificationKey.Length != FullAi91ExpectedLength)
        {
            issues.Add(Issue(IntegritySeverity.Warning, IntegrityIssueCode.InvalidAi91Length,
                $"AI 91: длина {code.VerificationKey.Length} (типично {FullAi91ExpectedLength})."));
        }

        if (code.VerificationCode.Length < FullAi92ExpectedLength - 4)
        {
            issues.Add(Issue(IntegritySeverity.Error, IntegrityIssueCode.TruncatedCryptoTail,
                $"AI 92 обрезан: длина {code.VerificationCode.Length} (типично {FullAi92ExpectedLength})."));
        }
        else if (code.VerificationCode.Length != FullAi92ExpectedLength)
        {
            issues.Add(Issue(IntegritySeverity.Warning, IntegrityIssueCode.InvalidAi92Length,
                $"AI 92: длина {code.VerificationCode.Length} (типично {FullAi92ExpectedLength})."));
        }

        if (!Base64ish.IsMatch(code.VerificationCode))
        {
            issues.Add(Issue(IntegritySeverity.Warning, IntegrityIssueCode.InvalidAi92Charset,
                "AI 92 содержит неожиданные символы (ожидается Base64)."));
        }
    }

    public static bool IsValidGtinCheckDigit(string gtin14)
    {
        if (gtin14.Length != 14 || !gtin14.All(char.IsDigit))
            return false;

        var sum = 0;
        for (var i = 0; i < 13; i++)
        {
            var digit = gtin14[12 - i] - '0';
            sum += (i % 2 == 0) ? digit * 3 : digit;
        }

        var check = (10 - sum % 10) % 10;
        return gtin14[13] - '0' == check;
    }

    private static IntegrityIssue Issue(IntegritySeverity severity, IntegrityIssueCode code, string message) =>
        new(severity, code, message);
}
