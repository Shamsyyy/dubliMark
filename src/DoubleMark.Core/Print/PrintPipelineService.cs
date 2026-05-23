namespace DoubleMark.Core.Print;

public sealed class PrintPipelineService
{
    private readonly MarkRenderService _renderService;
    private readonly PrintExportService _exportService;
    private readonly IMarkPrintService _printService;
    private readonly Func<DateTimeOffset> _now;
    private readonly Dictionary<string, DateTimeOffset> _lastPrintedByPayload = new(StringComparer.Ordinal);

    public PrintPipelineService(
        MarkRenderService renderService,
        PrintExportService exportService,
        IMarkPrintService printService,
        Func<DateTimeOffset>? now = null)
    {
        _renderService = renderService;
        _exportService = exportService;
        _printService = printService;
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public async Task<PrintPipelineResult> ProcessAsync(
        PrintPipelineRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.ParseResult.IsValid || request.ParseResult.Code == null)
            return new PrintPipelineResult { Error = request.ParseResult.ErrorMessage ?? "Невалидный код" };

        var timestamp = request.Timestamp ?? _now();
        if (request.Settings.LabelPrintDate is { } customDate)
            timestamp = new DateTimeOffset(customDate.Date.Add(timestamp.TimeOfDay));

        var render = _renderService.Render(new MarkRenderRequest
        {
            RawPayload = request.RawPayload,
            ParseResult = request.ParseResult,
            Template = request.Template,
            Source = request.Source,
            Timestamp = timestamp,
            Dpi = request.Settings.Dpi,
            ShowDate = request.Settings.LabelShowDate,
            ShowShipment = request.Settings.LabelShowShipment,
            ShowOrder = request.Settings.LabelShowOrder,
            ShipmentNumber = request.Settings.LabelShipmentNumber,
            OrderNumber = request.Settings.LabelOrderNumber
        });

        var shouldPrint = request.ForcePrint || request.Settings.AutoPrintEnabled;
        if (!shouldPrint)
            return PrintPipelineResult.NotPrinted(render);

        if (!request.AllowDuplicate && IsDuplicateBlocked(render.NormalizedPayload, timestamp,
                request.Settings.DuplicateProtectionSeconds))
        {
            return PrintPipelineResult.DuplicateBlocked(render);
        }

        if (request.Settings.DelayBeforePrintMs > 0)
            await Task.Delay(request.Settings.DelayBeforePrintMs, cancellationToken);

        var copies = Math.Max(1, request.Settings.Copies);
        var print = await _printService.PrintAsync(new PrintJobRequest
        {
            Render = render,
            PrinterName = request.Settings.PrinterName,
            Copies = copies,
            PrintWithoutConfirmation = request.Settings.PrintWithoutConfirmation
        }, cancellationToken);

        PrintExportResult? export = null;
        if (request.Settings.SaveFileBeforePrint)
        {
            export = _exportService.Save(new PrintExportRequest
            {
                Render = render,
                PrintRoot = request.Settings.PrintRoot,
                PrinterName = request.Settings.PrinterName,
                Copies = copies,
                Printed = print.Success,
                PrintError = print.Error
            });
        }

        if (print.Success)
            _lastPrintedByPayload[render.NormalizedPayload] = timestamp;

        return new PrintPipelineResult
        {
            Rendered = true,
            Printed = print.Success,
            Error = print.Success ? export?.Error : print.Error,
            Render = render,
            Export = export
        };
    }

    private bool IsDuplicateBlocked(string normalizedPayload, DateTimeOffset now, int seconds)
    {
        if (seconds <= 0)
            return false;

        if (!_lastPrintedByPayload.TryGetValue(normalizedPayload, out var lastPrinted))
            return false;

        return now - lastPrinted < TimeSpan.FromSeconds(seconds);
    }
}
