using DoubleMark.Core.Print;

namespace DoubleMark.Desktop.Services;

/// <summary>
/// Decodes/renders PDF pages while previously prepared chunks print on the UI thread.
/// Must run on a background thread (never block the WPF UI thread while waiting for print).
/// </summary>
internal sealed class PdfBatchPrintPipeline
{
    private readonly PdfBatchDecodeService _decodeService;
    private readonly MarkRenderService _renderService;
    private readonly Func<IReadOnlyList<MarkRenderResult>, IReadOnlyList<int>, PrintPipelineSettings, CancellationToken, Task<int>> _flushPrintAsync;
    private readonly Action<int, int, int, string, bool> _reportProgress;
    private readonly Action<PdfBatchPageRecord> _upsertRecord;
    private readonly Action<PdfPageDecodeResult>? _onDecodedForPreview;
    private readonly object _sessionLock = new();
    private readonly List<PdfPageDecodeResult> _verifyResults = new();
    private readonly object _verifyResultsLock = new();
    private Task _printTail = Task.CompletedTask;

    public PdfBatchPrintPipeline(
        PdfBatchDecodeService decodeService,
        MarkRenderService renderService,
        Func<IReadOnlyList<MarkRenderResult>, IReadOnlyList<int>, PrintPipelineSettings, CancellationToken, Task<int>> flushPrintAsync,
        Action<int, int, int, string, bool> reportProgress,
        Action<PdfBatchPageRecord> upsertRecord,
        Action<PdfPageDecodeResult>? onDecodedForPreview = null)
    {
        _decodeService = decodeService;
        _renderService = renderService;
        _flushPrintAsync = flushPrintAsync;
        _reportProgress = reportProgress;
        _upsertRecord = upsertRecord;
        _onDecodedForPreview = onDecodedForPreview;
    }

    public IReadOnlyList<PdfPageDecodeResult> VerifyResults
    {
        get
        {
            lock (_verifyResultsLock)
                return _verifyResults.ToList();
        }
    }

    public async Task RunAsync(
        PdfDocumentSession? session,
        IReadOnlyList<int> pages,
        PrintTemplate template,
        PrintPipelineSettings batchSettings,
        DateTimeOffset printTimestamp,
        int chunkSize,
        IReadOnlyDictionary<int, PdfPageDecodeResult>? cachedDecodes,
        CancellationToken token)
    {
        var renderBatch = new List<MarkRenderResult>(Math.Min(chunkSize, pages.Count));
        var batchPageNumbers = new List<int>(chunkSize);
        _printTail = Task.CompletedTask;

        for (var i = 0; i < pages.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var pageNumber = pages[i];
            PdfPageDecodeResult decoded;
            if (cachedDecodes != null &&
                cachedDecodes.TryGetValue(pageNumber, out var cached) &&
                cached.Success &&
                cached.ParseResult?.IsValid == true &&
                cached.ParseResult.Code != null)
            {
                _reportProgress(i + 1, pages.Count, pageNumber, "Готовим этикетку", false);
                decoded = cached;
            }
            else
            {
                _reportProgress(i + 1, pages.Count, pageNumber, "Читаем DataMatrix", false);
                decoded = await Task.Run(
                    () =>
                    {
                        if (session == null)
                            throw new InvalidOperationException($"Страница {pageNumber}: нет данных проверки и PDF-сессия не открыта.");

                        lock (_sessionLock)
                            return _decodeService.DecodePage(session, pageNumber, PdfRenderProfile.Thorough, template);
                    },
                    token).ConfigureAwait(false);
            }

            lock (_verifyResultsLock)
                _verifyResults.Add(decoded);
            _onDecodedForPreview?.Invoke(decoded);
            var record = PdfBatchPageRecord.FromDecode(pageNumber, decoded);
            _upsertRecord(record);

            if (record.Status == PdfBatchPageStatus.VerifiedOk &&
                decoded.RawPayload != null &&
                decoded.ParseResult != null)
            {
                var rawPayload = decoded.RawPayload;
                var parseResult = decoded.ParseResult;
                var render = await Task.Run(
                    () => _renderService.Render(new MarkRenderRequest
                    {
                        RawPayload = rawPayload,
                        ParseResult = parseResult,
                        Template = template,
                        Source = "PDF",
                        Timestamp = printTimestamp,
                        Dpi = batchSettings.Dpi,
                        ShowDate = batchSettings.LabelShowDate,
                        ShowShipment = batchSettings.LabelShowShipment,
                        ShowOrder = batchSettings.LabelShowOrder,
                        ShipmentNumber = batchSettings.LabelShipmentNumber,
                        OrderNumber = batchSettings.LabelOrderNumber
                    }),
                    token).ConfigureAwait(false);

                renderBatch.Add(render);
                batchPageNumbers.Add(pageNumber);

                if (renderBatch.Count >= chunkSize)
                {
                    _reportProgress(i + 1, pages.Count, pageNumber, "Печать + подготовка", false);
                    EnqueuePrint(renderBatch, batchPageNumbers, batchSettings, token);
                }
            }
        }

        if (renderBatch.Count > 0)
        {
            _reportProgress(pages.Count, pages.Count, pages[^1], "Печать + подготовка", true);
            EnqueuePrint(renderBatch, batchPageNumbers, batchSettings, token);
        }

        await _printTail.ConfigureAwait(false);
    }

    private void EnqueuePrint(
        List<MarkRenderResult> renderBatch,
        List<int> batchPageNumbers,
        PrintPipelineSettings batchSettings,
        CancellationToken token)
    {
        var chunkRenders = renderBatch.ToList();
        var chunkPages = batchPageNumbers.ToList();
        renderBatch.Clear();
        batchPageNumbers.Clear();

        var prior = _printTail;
        _printTail = PrintChunkAfterAsync(prior, chunkRenders, chunkPages, batchSettings, token);
    }

    private async Task PrintChunkAfterAsync(
        Task prior,
        IReadOnlyList<MarkRenderResult> chunkRenders,
        IReadOnlyList<int> chunkPages,
        PrintPipelineSettings batchSettings,
        CancellationToken token)
    {
        await prior.ConfigureAwait(false);
        if (token.IsCancellationRequested)
            return;

        try
        {
            await _flushPrintAsync(chunkRenders, chunkPages, batchSettings, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // skip remaining print chunks after cancel
        }
    }
}
