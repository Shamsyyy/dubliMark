namespace DubliMark.Desktop.Services;

public sealed class ImageDecodeResult
{
    public required string Raw { get; init; }
    public required string RawHex { get; init; }
    public int GsCount { get; init; }
    public int PayloadByteLength { get; init; }
    /// <summary>Bytes removed before AI 01 (ZXing/FNC1 preamble).</summary>
    public int PreambleStrippedBytes { get; init; }
    /// <summary>Offset of AI 01 in pre-normalization buffer; -1 if at start or unknown.</summary>
    public int Ai01Offset { get; init; } = -1;
    public string? NormalizeNote { get; init; }
}
