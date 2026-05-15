namespace DubliMark.Core.Models;

public record MarkingCode
{
    public required string Gtin { get; init; }              // AI 01, 14 digits
    public required string Serial { get; init; }            // AI 21, up to GS
    public string? VerificationKey { get; init; }           // AI 91
    public string? VerificationCode { get; init; }          // AI 92
    public required string RawData { get; init; }           // original with GS
    public required string RawDataHex { get; init; }        // hex for debugging
}

public record ParseResult
{
    public bool IsValid { get; init; }
    public MarkingCode? Code { get; init; }
    public string? ErrorMessage { get; init; }
    public ParseErrorCode? ErrorCode { get; init; }
}

public enum ParseErrorCode
{
    Empty,
    NoGtin,
    InvalidGtinLength,
    NoSerial,
    NoGsSeparator,        // no GS separator — scanner is stripping it
    UnknownAi
}
