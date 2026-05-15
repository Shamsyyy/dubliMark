namespace DubliMark.Core.Parsing;

/// <summary>Result of stripping decoder preamble and locating AI 01.</summary>
public sealed class Gs1NormalizeResult
{
    public required string Payload { get; init; }
    public required byte[] Bytes { get; init; }
    public int StrippedPrefixBytes { get; init; }
    /// <summary>Index of AI 01 in the original byte buffer before slicing; -1 if not found.</summary>
    public int Ai01Offset { get; init; } = -1;
    public bool FoundAi01 { get; init; }
}
