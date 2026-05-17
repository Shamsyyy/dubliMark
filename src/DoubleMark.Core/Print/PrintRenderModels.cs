using DoubleMark.Core.Models;

namespace DoubleMark.Core.Print;

public sealed record MarkRenderRequest
{
    public required string RawPayload { get; init; }
    public required ParseResult ParseResult { get; init; }
    public required PrintTemplate Template { get; init; }
    public string Source { get; init; } = "Manual";
    public DateTimeOffset? Timestamp { get; init; }
    public int Dpi { get; init; } = 300;
}

public sealed record MarkRenderResult
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string Source { get; init; }
    public required PrintTemplate Template { get; init; }
    public required string RawPayload { get; init; }
    public required string NormalizedPayload { get; init; }
    public required string RawPayloadEscaped { get; init; }
    public required string NormalizedPayloadEscaped { get; init; }
    public required string RawHex { get; init; }
    public required int GsCount { get; init; }
    public required bool HasAi01 { get; init; }
    public required bool HasAi21 { get; init; }
    public required bool HasAi91 { get; init; }
    public required bool HasAi92 { get; init; }
    public required bool HasAi93 { get; init; }
    public required string CodeType { get; init; }
    public required string Gtin { get; init; }
    public required string Serial { get; init; }
    public string? Ai91 { get; init; }
    public string? Ai92 { get; init; }
    public string? Ai93 { get; init; }
    public required byte[] PngBytes { get; init; }
    public required byte[] PdfBytes { get; init; }
    public required int PngWidthPx { get; init; }
    public required int PngHeightPx { get; init; }
    public required double PdfWidthPt { get; init; }
    public required double PdfHeightPt { get; init; }
    public required int Dpi { get; init; }
}
