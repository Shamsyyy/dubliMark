namespace DubliMark.Core.Print;

public sealed record PrintExportRequest
{
    public required MarkRenderResult Render { get; init; }
    public string? PrintRoot { get; init; }
    public string? PrinterName { get; init; }
    public int Copies { get; init; } = 1;
    public bool Printed { get; init; }
    public string? PrintError { get; init; }
}

public sealed record PrintExportResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? DirectoryPath { get; init; }
    public PrintFileSet? Files { get; init; }
}

public sealed record PrintFileSet
{
    public required string PdfPath { get; init; }
    public required string PngPath { get; init; }
    public required string JsonPath { get; init; }

    public IReadOnlyList<string> All => new[] { PdfPath, PngPath, JsonPath };
}
