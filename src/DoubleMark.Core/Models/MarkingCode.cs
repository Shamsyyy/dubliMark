namespace DoubleMark.Core.Models;

/// <summary>
/// ЧЗ DataMatrix: полный код (~80+ байт, AI 91/92) или короткий (~30 байт, 22×22–24×24 матрица).
/// </summary>
public enum MarkingCodeType
{
    Unknown,
    /// <summary>01+GTIN+21+serial+GS+91+key+GS+92+crypto</summary>
    Full,
    /// <summary>01+GTIN+21+serial, опционально GS+93 (4 символа) без криптохвоста 91/92</summary>
    Short
}

public record MarkingCode
{
    public required string Gtin { get; init; }              // AI 01, 14 digits
    public required string Serial { get; init; }            // AI 21, up to GS
    public string? VerificationKey { get; init; }           // AI 91 (только Full)
    public string? VerificationCode { get; init; }          // AI 92 (только Full)
    /// <summary>AI 93 — доп. поле в коротком коде (часто 4 символа проверки), не криптохвост 92.</summary>
    public string? AdditionalField93 { get; init; }
    public MarkingCodeType CodeType { get; init; } = MarkingCodeType.Unknown;
    public required string RawData { get; init; }           // original with GS
    public required string RawDataHex { get; init; }        // hex for debugging
}

public record ParseResult
{
    public bool IsValid { get; init; }
    public MarkingCode? Code { get; init; }
    public string? ErrorMessage { get; init; }
    public ParseErrorCode? ErrorCode { get; init; }
    /// <summary>Информационные замечания (не ошибки), напр. неполный проверочный хвост короткого кода.</summary>
    public IReadOnlyList<string> InfoMessages { get; init; } = Array.Empty<string>();
}

public enum ParseErrorCode
{
    Empty,
    NoGtin,
    InvalidGtinLength,
    NoSerial,
    NoGsSeparator,        // no GS separator — scanner may be stripping FNC1
    TruncatedPayload,     // incomplete full code (91/92 started but cut off)
    UnknownAi
}
