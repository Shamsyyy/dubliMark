using DoubleMark.Core.Models;

namespace DoubleMark.Core.Export;

public sealed record MarkExportRequest
{
    public required string RawPayload { get; init; }
    public required ParseResult ParseResult { get; init; }
    public string Source { get; init; } = "Manual";
    public string? ExportRoot { get; init; }
    public string? DiagnosticsRoot { get; init; }
    public DateTimeOffset? Timestamp { get; init; }
    public bool SaveInvalidDiagnostics { get; init; } = true;
}

public sealed record MarkExportResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? ExportDirectory { get; init; }
    public string? DiagnosticsFilePath { get; init; }
    public MarkExportFileSet? Files { get; init; }
    public string? NormalizedPayload { get; init; }
}

public sealed record MarkExportFileSet
{
    public required string TextPath { get; init; }
    public required string JsonPath { get; init; }
    public required string PngPath { get; init; }
    public required string PdfPath { get; init; }

    public IReadOnlyList<string> All => new[] { TextPath, JsonPath, PngPath, PdfPath };
}

public interface IDataMatrixArtifactWriter
{
    void WritePng(string payload, string path);
    void WritePdf(string payload, MarkExportPdfInfo info, string path);
}

public sealed record MarkExportPdfInfo
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string Source { get; init; }
    public required string CodeType { get; init; }
    public required string Gtin { get; init; }
    public required string Serial { get; init; }
    public required int GsCount { get; init; }
    public bool HasAi91 { get; init; }
    public bool HasAi92 { get; init; }
    public bool HasAi93 { get; init; }
    public int Ai92Length { get; init; }
}
