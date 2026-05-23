namespace DoubleMark.Core.Print;

public sealed record PrintPipelineSettings
{
    public bool AutoPrintEnabled { get; init; }
    public string? PrinterName { get; init; }
    public int Copies { get; init; } = 1;
    public bool PrintWithoutConfirmation { get; init; }
    public int DelayBeforePrintMs { get; init; }
    public int DuplicateProtectionSeconds { get; init; } = 5;
    public bool SaveFileBeforePrint { get; init; } = true;
    public string? PrintRoot { get; init; }
    public string? DefaultTemplateName { get; init; }
    public int Dpi { get; init; } = 300;
    public bool LabelShowDate { get; init; } = true;
    public bool LabelShowShipment { get; init; }
    public bool LabelShowOrder { get; init; }
    public string? LabelShipmentNumber { get; init; }
    public string? LabelOrderNumber { get; init; }
    public DateTime? LabelPrintDate { get; init; }
}

public sealed record PrintJobRequest
{
    public required MarkRenderResult Render { get; init; }
    public string? PrinterName { get; init; }
    public int Copies { get; init; } = 1;
    public bool PrintWithoutConfirmation { get; init; }
}

public sealed record PrintJobResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
}

public interface IMarkPrintService
{
    Task<PrintJobResult> PrintAsync(PrintJobRequest request, CancellationToken cancellationToken = default);
}

public sealed record PrintPipelineRequest
{
    public required string RawPayload { get; init; }
    public required DoubleMark.Core.Models.ParseResult ParseResult { get; init; }
    public required string Source { get; init; }
    public required PrintTemplate Template { get; init; }
    public required PrintPipelineSettings Settings { get; init; }
    public DateTimeOffset? Timestamp { get; init; }
    public bool ForcePrint { get; init; }
    public bool AllowDuplicate { get; init; }
}

public sealed record PrintPipelineResult
{
    public bool Rendered { get; init; }
    public bool Printed { get; init; }
    public bool BlockedDuplicate { get; init; }
    public string? Error { get; init; }
    public MarkRenderResult? Render { get; init; }
    public PrintExportResult? Export { get; init; }

    public static PrintPipelineResult NotPrinted(MarkRenderResult render) =>
        new() { Rendered = true, Render = render };

    public static PrintPipelineResult DuplicateBlocked(MarkRenderResult render) =>
        new()
        {
            Rendered = true,
            Render = render,
            BlockedDuplicate = true,
            Error = "Этот ЧЗ уже печатался недавно. Повторная печать заблокирована защитой от дублей."
        };
}
