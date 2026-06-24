using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DoubleMark.Core.Print;
using DoubleMark.Desktop.Services;
using DoubleMark.Desktop.Services.Account;
using DoubleMark.Desktop.Views;
using Microsoft.Win32;

namespace DoubleMark.Desktop;

public partial class MainWindow
{
    private const int PdfPrintChunkSize = 64;

    private PdfPrintView? _pdfPrintView;
    private readonly PdfBatchDecodeService _pdfBatchDecodeService = new();
    private readonly PdfBatchHistoryService _pdfBatchHistoryService = new();
    private readonly object _pdfBatchRecordsLock = new();
    private CancellationTokenSource? _pdfBatchCts;
    private CancellationTokenSource? _pdfInfoCts;
    private int _pdfJobGeneration;
    private PdfBatchAnalyzeResult? _lastPdfAnalyzeResult;
    private readonly List<PdfBatchPageRecord> _pdfBatchRecords = new();
    private string? _selectedPdfPath;
    private string? _activePdfHistoryJobId;
    private string? _lastPdfPageRange;
    private int _lastPdfTotalPages;
    private int _lastPdfSelectedPages;
    private int _pdfKnownTotalPages;
    private string _pdfPageCountText = "";
    private string _pdfPrintStatus = "Выберите PDF с кодами Честного ЗНАКа.";
    private string _pdfPrintSummary = "—";
    private bool _pdfPrintBusy;
    private double _pdfPrintProgress;
    private bool _pdfCanPrint;
    private ImageSource? _cachedPdfPreview;
    private DateTime _lastPdfUiUpdateUtc = DateTime.MinValue;

    private PdfPrintView GetPdfPrintView()
    {
        if (_pdfPrintView != null)
            return _pdfPrintView;

        _pdfPrintView = new PdfPrintView();
        _pdfPrintView.BrowsePdfRequested += OnPdfBrowseClick;
        _pdfPrintView.AnalyzePdfRequested += OnPdfAnalyzeClick;
        _pdfPrintView.PrintPdfRequested += OnPdfPrintClick;
        _pdfPrintView.CancelPdfRequested += OnPdfCancelClick;
        _pdfPrintView.OpenAllResultsRequested += OnPdfOpenAllResultsClick;
        _pdfPrintView.OpenProblemsRequested += OnPdfOpenProblemsClick;
        _pdfPrintView.OpenHistoryJobRequested += (_, id) => OpenPdfHistoryJob(id, PdfBatchResultsMode.All);
        _pdfPrintView.OpenHistoryProblemsRequested += (_, id) => OpenPdfHistoryJob(id, PdfBatchResultsMode.ProblemsOnly);
        _pdfPrintView.TemplateChanged += OnPdfPrintTemplateChanged;
        _pdfPrintView.PrinterChanged += OnPdfPrintPrinterChanged;
        _pdfPrintView.PageRangeChanged += (_, _) => OnPdfPageRangeChanged();
        SyncPdfPrintPageState();
        return _pdfPrintView;
    }

    private async void OnPdfBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (_pdfPrintBusy)
            CancelPdfJob("Предыдущая операция отменена — выбран другой PDF.");

        var dlg = new OpenFileDialog
        {
            Title = "Выберите PDF с кодами Честного ЗНАКа",
            Filter = "PDF|*.pdf|Все файлы|*.*"
        };

        if (dlg.ShowDialog() != true)
            return;

        _selectedPdfPath = dlg.FileName;
        _lastPdfAnalyzeResult = null;
        _pdfBatchRecords.Clear();
        _activePdfHistoryJobId = null;
        _cachedPdfPreview = null;
        _pdfKnownTotalPages = 0;
        _pdfPageCountText = "";
        _pdfCanPrint = true;
        _pdfPrintSummary = Path.GetFileName(_selectedPdfPath);
        _pdfPrintStatus = "Читаем PDF…";
        if (_pdfPrintView != null)
            _pdfPrintView.ClearPageRange();
        SyncPdfPrintPageState();

        await RefreshPdfFileInfoAsync(_selectedPdfPath);
    }

    private void OnPdfPageRangeChanged()
    {
        UpdatePdfPageCountSummary();
        SyncPdfPrintPageState();
    }

    private async Task RefreshPdfFileInfoAsync(string pdfPath)
    {
        _pdfInfoCts?.Cancel();
        _pdfInfoCts?.Dispose();
        _pdfInfoCts = new CancellationTokenSource();
        var token = _pdfInfoCts.Token;

        try
        {
            var totalPages = await Task.Run(() => _pdfBatchDecodeService.GetPageCount(pdfPath), token);
            if (token.IsCancellationRequested || !string.Equals(_selectedPdfPath, pdfPath, StringComparison.OrdinalIgnoreCase))
                return;

            _pdfKnownTotalPages = totalPages;
            _lastPdfTotalPages = totalPages;
            UpdatePdfPageCountSummary();
            _pdfPrintStatus = "PDF выбран. «Печать пакета» сразу декодирует и печатает без отдельной проверки.";
            _pdfCanPrint = true;
        }
        catch (OperationCanceledException)
        {
            // superseded by another browse
        }
        catch (Exception ex)
        {
            _pdfKnownTotalPages = 0;
            _pdfPageCountText = "";
            _pdfPrintStatus = "Не удалось прочитать PDF: " + ex.Message;
            _pdfCanPrint = false;
        }
        finally
        {
            SyncPdfPrintPageState();
        }
    }

    private void UpdatePdfPageCountSummary()
    {
        if (_pdfKnownTotalPages <= 0 || string.IsNullOrWhiteSpace(_selectedPdfPath))
        {
            _pdfPageCountText = "";
            return;
        }

        var pageRange = _pdfPrintView?.PageRange ?? string.Empty;
        if (PageRangeParser.TryParse(pageRange, _pdfKnownTotalPages, out var pages, out var rangeError))
        {
            _lastPdfSelectedPages = pages.Count;
            _pdfPageCountText = string.IsNullOrWhiteSpace(pageRange)
                ? $"Страниц в PDF: {_pdfKnownTotalPages} · к печати: {pages.Count} (все)"
                : $"Страниц в PDF: {_pdfKnownTotalPages} · к печати: {pages.Count}";
        }
        else
        {
            _pdfPageCountText = $"Страниц в PDF: {_pdfKnownTotalPages} · диапазон: {rangeError}";
        }

        if (!_pdfPrintBusy && _pdfBatchRecords.Count == 0 && !string.IsNullOrWhiteSpace(_selectedPdfPath))
            _pdfPrintSummary = $"{Path.GetFileName(_selectedPdfPath)} · {_pdfPageCountText}";
    }

    private void CancelPdfJob(string? statusMessage = null)
    {
        _pdfJobGeneration++;
        _pdfBatchCts?.Cancel();
        _pdfBatchCts?.Dispose();
        _pdfBatchCts = null;

        if (!_pdfPrintBusy)
            return;

        _pdfPrintBusy = false;
        _pdfPrintProgress = 0;
        _pdfCanPrint = !string.IsNullOrWhiteSpace(_selectedPdfPath) && File.Exists(_selectedPdfPath);
        if (!string.IsNullOrWhiteSpace(statusMessage))
            _pdfPrintStatus = statusMessage;
        SyncPdfPrintPageState();
    }

    private async void OnPdfAnalyzeClick(object? sender, RoutedEventArgs e) =>
        await RunPdfVerifyAsync();

    private async void OnPdfPrintClick(object? sender, RoutedEventArgs e) =>
        await RunPdfPrintAsync();

    private void OnPdfCancelClick(object? sender, RoutedEventArgs e) =>
        CancelPdfJob("Операция отменена.");

    private void OnPdfOpenAllResultsClick(object? sender, RoutedEventArgs e) =>
        ShowPdfBatchResultsWindow(PdfBatchResultsMode.All);

    private void OnPdfOpenProblemsClick(object? sender, RoutedEventArgs e) =>
        ShowPdfBatchResultsWindow(PdfBatchResultsMode.ProblemsOnly);

    private void OpenPdfHistoryJob(string jobId, PdfBatchResultsMode mode)
    {
        if (_pdfPrintBusy)
            CancelPdfJob("Предыдущая операция отменена.");

        var snapshot = _pdfBatchHistoryService.Load(jobId);
        if (snapshot == null)
        {
            ShowToast("Запись истории не найдена.", ToastKind.Warning);
            SyncPdfPrintPageState();
            return;
        }

        _activePdfHistoryJobId = snapshot.Id;
        _selectedPdfPath = snapshot.PdfPath;
        _lastPdfPageRange = snapshot.PageRange;
        _lastPdfTotalPages = snapshot.TotalPagesInFile;
        _lastPdfSelectedPages = snapshot.SelectedPages;
        _pdfBatchRecords.Clear();
        _pdfBatchRecords.AddRange(snapshot.Pages.Select(PdfBatchHistoryService.FromDto));
        _pdfKnownTotalPages = snapshot.TotalPagesInFile;
        UpdatePdfPageCountSummary();

        var pdfMissing = !File.Exists(snapshot.PdfPath);
        var subtitle = pdfMissing
            ? $"Файл не найден: {snapshot.PdfPath}. Перепроверка и печать недоступны, но список страниц сохранён."
            : snapshot.PdfPath;

        if (pdfMissing && mode == PdfBatchResultsMode.ProblemsOnly)
            MessageBox.Show(this,
                "PDF-файл перемещён или удалён. Список проблемных страниц можно посмотреть, но перепечатать нельзя.",
                "PDF-пакет",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

        ShowPdfBatchResultsWindow(mode, snapshot.PdfPath, snapshot.Id, subtitle, readOnly: pdfMissing);
        SyncPdfPrintPageState();
    }

    private void ShowPdfBatchResultsWindow(
        PdfBatchResultsMode mode,
        string? pdfPath = null,
        string? historyJobId = null,
        string? subtitle = null,
        bool readOnly = false)
    {
        if (_pdfBatchRecords.Count == 0)
            return;

        pdfPath ??= _selectedPdfPath ?? "";
        var window = new PdfBatchResultsWindow(
            mode,
            _pdfBatchRecords,
            readOnly ? null : RetryPdfBatchPagesAsync,
            pdfPath,
            historyJobId ?? _activePdfHistoryJobId,
            subtitle,
            readOnly)
        {
            Owner = this
        };
        window.Closed += (_, _) => SyncPdfPrintPageState();
        window.Show();
    }

    private void OnPdfPrintTemplateChanged(object? sender, string? templateName)
    {
        if (string.IsNullOrWhiteSpace(templateName))
            return;

        SetActivePrintTemplate(templateName, showToast: false);
        _cachedPdfPreview = null;
        SyncPdfPrintPageState();
    }

    private void OnPdfPrintPrinterChanged(object? sender, string? printerName)
    {
        _settings.PrinterName = string.Equals(printerName, "По умолчанию", StringComparison.OrdinalIgnoreCase)
            ? null
            : printerName;
        _settings.Save();
        SyncPdfPrintPageState();
    }

    private sealed record PdfJobContext(
        string PdfPath,
        IReadOnlyList<int> Pages,
        int TotalPages,
        CancellationToken Token,
        int Generation);

    private async Task RunPdfVerifyAsync()
    {
        if (!await EnsureSubscriptionForFeatureAsync("Печать из PDF"))
            return;

        var job = await TryBeginPdfJobAsync();
        if (job == null)
            return;

        var (pdfPath, pages, totalPages, token, jobGeneration) = job;
        _activePdfHistoryJobId = null;
        _lastPdfPageRange = _pdfPrintView?.PageRange;
        _lastPdfTotalPages = totalPages;
        _lastPdfSelectedPages = pages.Count;

        if (pages.Count > 200)
        {
            var warn = MessageBox.Show(
                $"Выбрано {pages.Count} страниц. Проверка может занять много времени.\n\n" +
                "Для массовой печати используйте «Печать пакета» без предварительной проверки.",
                "PDF-пакет",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (warn != MessageBoxResult.OK)
            {
                FinishPdfJob(jobGeneration);
                return;
            }
        }

        try
        {
            _pdfPrintStatus = $"Проверяем {pages.Count} стр. из {totalPages}…";

            var progress = new Progress<PdfBatchProgress>(report =>
                ReportPdfProgress(report.CompletedCount, report.TotalPages, report.CurrentPage, report.Stage));

            var template = ResolveActiveTemplate();

            _lastPdfAnalyzeResult = await _pdfBatchDecodeService.AnalyzeAsync(
                pdfPath,
                pages,
                PdfRenderProfile.Thorough,
                template,
                progress,
                token);

            _pdfBatchRecords.Clear();
            foreach (var decoded in _lastPdfAnalyzeResult.Results)
                UpsertPdfBatchRecord(PdfBatchPageRecord.FromDecode(decoded.PageNumber, decoded));

            UpdatePreviewFromFirstValidPage(_lastPdfAnalyzeResult);
            _pdfCanPrint = _pdfBatchRecords.Any(r => r.Status == PdfBatchPageStatus.VerifiedOk);
            ApplyPdfSummaryFromRecords(totalPages, pages.Count);
            _pdfPrintStatus = _pdfCanPrint
                ? "PDF проверен. Можно печатать пакет."
                : "Не найдено валидных кодов на выбранных страницах.";

            SaveCurrentPdfBatchJob(PdfBatchJobKind.Verify, pdfPath);
        }
        catch (OperationCanceledException)
        {
            _pdfPrintStatus = "Проверка отменена.";
        }
        catch (Exception ex)
        {
            LoggingService.Error("PdfBatch", "Verify failed", ex);
            _pdfPrintStatus = "Ошибка проверки PDF: " + ex.Message;
        }
        finally
        {
            FinishPdfJob(jobGeneration);
        }
    }

    private async Task RunPdfPrintAsync()
    {
        if (!await EnsureSubscriptionForFeatureAsync("Печать из PDF"))
            return;

        var job = await TryBeginPdfJobAsync();
        if (job == null)
            return;

        var (pdfPath, pages, totalPages, token, jobGeneration) = job;
        _activePdfHistoryJobId = null;
        _lastPdfPageRange = _pdfPrintView?.PageRange;
        _lastPdfTotalPages = totalPages;
        _lastPdfSelectedPages = pages.Count;

        var decodeCache = BuildPdfDecodeCache(pdfPath, pages);
        var cachedCount = pages.Count(p => decodeCache.ContainsKey(p));

        var confirm = MessageBox.Show(
            cachedCount == pages.Count
                ? $"Напечатать {pages.Count} стр. подряд?\n\n" +
                  "Коды уже проверены — повторное чтение PDF не выполняется."
                : cachedCount > 0
                    ? $"Напечатать {pages.Count} стр. подряд?\n\n" +
                      $"{cachedCount} стр. из проверки, остальные {pages.Count - cachedCount} будут прочитаны из PDF " +
                      "(тот же режим, что и при «Проверить PDF»)."
                    : $"Напечатать {pages.Count} стр. подряд?\n\n" +
                      "Код читается из PDF тем же способом, что и при проверке, и отправляется на печать.",
            "Печать PDF-пакета",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            FinishPdfJob(jobGeneration);
            return;
        }

        var template = ResolveActiveTemplate();
        var batchSettings = BuildPdfBatchPrintSettings();
        var printTimestamp = DateTimeOffset.Now;
        var needsLiveDecode = cachedCount < pages.Count;

        lock (_pdfBatchRecordsLock)
            _pdfBatchRecords.RemoveAll(r => !pages.Contains(r.PageNumber));

        try
        {
            using var session = needsLiveDecode
                ? await PdfDocumentSession.OpenFileAsync(pdfPath, PdfRenderProfile.Thorough, token)
                : null;

            var pipeline = new PdfBatchPrintPipeline(
                _pdfBatchDecodeService,
                _markRenderService,
                FlushPdfPrintChunkAsync,
                ReportPdfProgress,
                UpsertPdfBatchRecordThreadSafe,
                decoded => Dispatcher.BeginInvoke(() => TryUpdatePreviewFromDecode(decoded)));

            await Task.Run(async () =>
            {
                await pipeline.RunAsync(
                    session,
                    pages,
                    template,
                    batchSettings,
                    printTimestamp,
                    PdfPrintChunkSize,
                    decodeCache,
                    token).ConfigureAwait(false);
            }, token).ConfigureAwait(true);

            var verifyResults = pipeline.VerifyResults;
            _lastPdfAnalyzeResult = new PdfBatchAnalyzeResult(totalPages, pages, verifyResults);
            ApplyPdfSummaryFromRecords(totalPages, pages.Count);

            var printed = _pdfBatchRecords.Count(r => r.Status == PdfBatchPageStatus.Printed);
            var problems = _pdfBatchRecords.Count(r => r.IsProblem);
            _pdfPrintStatus = printed == 0
                ? "Печать не выполнена: коды не распознаны или принтер недоступен."
                : problems == 0
                    ? $"Пакетная печать завершена: {printed} этикеток."
                    : BuildPdfPrintStatusMessage(printed, problems);
            ShowToast(_pdfPrintStatus, printed > 0 ? ToastKind.Success : ToastKind.Warning);
            SaveCurrentPdfBatchJob(PdfBatchJobKind.Print, pdfPath);
            SyncPrintPageState();
        }
        catch (OperationCanceledException)
        {
            var printed = _pdfBatchRecords.Count(r => r.Status == PdfBatchPageStatus.Printed);
            _pdfPrintStatus = $"Печать отменена. Напечатано {printed} этикеток.";
            if (_pdfBatchRecords.Count > 0)
                SaveCurrentPdfBatchJob(PdfBatchJobKind.Print, pdfPath);
        }
        catch (Exception ex)
        {
            LoggingService.Error("PdfBatch", "Print failed", ex);
            _pdfPrintStatus = "Ошибка печати PDF: " + ex.Message;
        }
        finally
        {
            FinishPdfJob(jobGeneration);
        }
    }

    private async Task<PdfBatchRetryResult> RetryPdfBatchPagesAsync(PdfBatchRetryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PdfPath) || !File.Exists(request.PdfPath))
            throw new InvalidOperationException("PDF-файл не найден. Укажите исходный файл через «Обзор…».");

        if (request.PrintAfter &&
            !FeatureAccessRules.CanUsePremiumFeature(_accountSnapshot.Subscription) &&
            !await EnsureSubscriptionForFeatureAsync("Печать из PDF"))
        {
            throw new InvalidOperationException("Нет активной подписки для печати.");
        }

        return await Task.Run(async () =>
            await RetryPdfBatchPagesCoreAsync(request).ConfigureAwait(false)).ConfigureAwait(true);
    }

    private async Task<PdfBatchRetryResult> RetryPdfBatchPagesCoreAsync(PdfBatchRetryRequest request)
    {
        var pages = request.PageNumbers.OrderBy(n => n).ToList();
        var template = ResolveActiveTemplate();
        var batchSettings = BuildPdfBatchPrintSettings();
        var printTimestamp = DateTimeOffset.Now;
        var rechecked = 0;
        var printed = 0;
        var renderBatch = new List<MarkRenderResult>();
        var batchPageNumbers = new List<int>();

        using var session = await PdfDocumentSession.OpenFileAsync(
            request.PdfPath,
            PdfRenderProfile.Thorough,
            request.CancellationToken).ConfigureAwait(false);

        for (var i = 0; i < pages.Count; i++)
        {
            request.CancellationToken.ThrowIfCancellationRequested();
            var pageNumber = pages[i];
            request.Progress?.Report(new PdfBatchProgress(pageNumber, pages.Count, $"Стр. {pageNumber}: читаем DataMatrix", i));

            var decoded = await Task.Run(
                () => _pdfBatchDecodeService.DecodePage(session, pageNumber, PdfRenderProfile.Thorough, template),
                request.CancellationToken).ConfigureAwait(false);
            rechecked++;

            request.Progress?.Report(new PdfBatchProgress(pageNumber, pages.Count, $"Стр. {pageNumber}: готово", i + 1));

            var record = PdfBatchPageRecord.FromDecode(pageNumber, decoded);
            await Dispatcher.InvokeAsync(() => UpsertPdfBatchRecord(record), DispatcherPriority.Background);

            if (request.PrintAfter &&
                record.Status == PdfBatchPageStatus.VerifiedOk &&
                decoded.RawPayload != null &&
                decoded.ParseResult != null)
            {
                request.Progress?.Report(new PdfBatchProgress(pageNumber, pages.Count, "Готовим этикетку", i + 1));
                var render = await Task.Run(
                    () => _markRenderService.Render(new MarkRenderRequest
                    {
                        RawPayload = decoded.RawPayload,
                        ParseResult = decoded.ParseResult,
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
                    request.CancellationToken).ConfigureAwait(false);

                renderBatch.Add(render);
                batchPageNumbers.Add(pageNumber);

                if (renderBatch.Count >= PdfPrintChunkSize)
                {
                    request.Progress?.Report(new PdfBatchProgress(pageNumber, pages.Count, "Печать", i + 1));
                    printed += await FlushPdfPrintBatchAsync(renderBatch, batchPageNumbers, batchSettings, request.CancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }

        if (request.PrintAfter && renderBatch.Count > 0)
        {
            request.Progress?.Report(new PdfBatchProgress(pages[^1], pages.Count, "Печать", pages.Count));
            printed += await FlushPdfPrintBatchAsync(renderBatch, batchPageNumbers, batchSettings, request.CancellationToken)
                .ConfigureAwait(false);
        }

        List<PdfBatchPageRecord>? recordsSnapshot = null;
        await Dispatcher.InvokeAsync(() =>
        {
            ApplyPdfSummaryFromRecords(_lastPdfTotalPages, _lastPdfSelectedPages);
            var jobId = request.HistoryJobId ?? _activePdfHistoryJobId;
            if (!string.IsNullOrWhiteSpace(jobId))
                _pdfBatchHistoryService.UpdatePages(jobId, _pdfBatchRecords);
            else if (!string.IsNullOrWhiteSpace(_selectedPdfPath))
                SaveCurrentPdfBatchJob(PdfBatchJobKind.Print, _selectedPdfPath);
            recordsSnapshot = _pdfBatchRecords.ToList();
            SyncPdfPrintPageState();
        });

        return new PdfBatchRetryResult
        {
            Rechecked = rechecked,
            Printed = printed,
            StillProblem = recordsSnapshot!.Count(r => r.IsProblem),
            Records = recordsSnapshot!
        };
    }

    private void SaveCurrentPdfBatchJob(PdfBatchJobKind kind, string pdfPath)
    {
        if (_pdfBatchRecords.Count == 0)
            return;

        var template = ResolveActiveTemplate();
        var snapshot = new PdfBatchJobSnapshot
        {
            Id = _activePdfHistoryJobId ?? Guid.NewGuid().ToString("N"),
            CompletedAt = DateTimeOffset.Now,
            Kind = kind,
            PdfPath = pdfPath,
            PdfFileName = Path.GetFileName(pdfPath),
            PageRange = _lastPdfPageRange,
            TotalPagesInFile = _lastPdfTotalPages,
            SelectedPages = _lastPdfSelectedPages,
            PrintedCount = _pdfBatchRecords.Count(r => r.Status == PdfBatchPageStatus.Printed),
            ProblemCount = _pdfBatchRecords.Count(r => r.IsProblem),
            TemplateName = template.Name,
            PrinterName = _settings.PrinterName,
            Pages = _pdfBatchRecords.Select(PdfBatchHistoryService.ToDto).ToList()
        };

        _pdfBatchHistoryService.Save(snapshot);
        _activePdfHistoryJobId = snapshot.Id;
    }

    private PrintPipelineSettings BuildPdfBatchPrintSettings() =>
        _settings.ToPrintPipelineSettings() with
        {
            AutoPrintEnabled = true,
            PrintWithoutConfirmation = true,
            DuplicateProtectionSeconds = 0,
            DelayBeforePrintMs = 0,
            SaveFileBeforePrint = false
        };

    private Dictionary<int, PdfPageDecodeResult> BuildPdfDecodeCache(string pdfPath, IReadOnlyList<int> pages)
    {
        var cache = new Dictionary<int, PdfPageDecodeResult>();
        if (string.IsNullOrWhiteSpace(pdfPath) ||
            string.IsNullOrWhiteSpace(_selectedPdfPath) ||
            !string.Equals(_selectedPdfPath, pdfPath, StringComparison.OrdinalIgnoreCase))
        {
            return cache;
        }

        lock (_pdfBatchRecordsLock)
        {
            foreach (var record in _pdfBatchRecords)
            {
                if (!pages.Contains(record.PageNumber))
                    continue;

                if (record.RawPayload == null ||
                    record.ParseResult?.IsValid != true ||
                    record.ParseResult.Code == null)
                    continue;

                if (record.Status is not PdfBatchPageStatus.VerifiedOk and not PdfBatchPageStatus.Printed)
                    continue;

                cache[record.PageNumber] = new PdfPageDecodeResult(
                    record.PageNumber,
                    true,
                    record.RawPayload,
                    record.ParseResult,
                    null);
            }
        }

        if (_lastPdfAnalyzeResult == null)
            return cache;

        foreach (var decoded in _lastPdfAnalyzeResult.Results)
        {
            if (!pages.Contains(decoded.PageNumber))
                continue;

            if (!decoded.Success ||
                decoded.ParseResult?.IsValid != true ||
                decoded.ParseResult.Code == null)
                continue;

            cache.TryAdd(decoded.PageNumber, decoded);
        }

        return cache;
    }

    private void UpsertPdfBatchRecordThreadSafe(PdfBatchPageRecord record)
    {
        lock (_pdfBatchRecordsLock)
            UpsertPdfBatchRecord(record);
    }

    private void UpsertPdfBatchRecord(PdfBatchPageRecord record)
    {
        var idx = _pdfBatchRecords.FindIndex(r => r.PageNumber == record.PageNumber);
        if (idx >= 0)
            _pdfBatchRecords[idx] = record;
        else
            _pdfBatchRecords.Add(record);
    }

    private void SetPdfBatchRecordStatus(int pageNumber, PdfBatchPageStatus status, string? reason)
    {
        lock (_pdfBatchRecordsLock)
            SetPdfBatchRecordStatusUnlocked(pageNumber, status, reason);
    }

    private void SetPdfBatchRecordStatusUnlocked(int pageNumber, PdfBatchPageStatus status, string? reason)
    {
        var idx = _pdfBatchRecords.FindIndex(r => r.PageNumber == pageNumber);
        if (idx < 0)
            return;

        var existing = _pdfBatchRecords[idx];
        _pdfBatchRecords[idx] = new PdfBatchPageRecord
        {
            PageNumber = existing.PageNumber,
            Status = status,
            Reason = reason,
            Gtin = existing.Gtin,
            Serial = existing.Serial,
            RawPayload = existing.RawPayload,
            ParseResult = existing.ParseResult
        };
    }

    private async Task<int> FlushPdfPrintChunkAsync(
        IReadOnlyList<MarkRenderResult> chunk,
        IReadOnlyList<int> pages,
        PrintPipelineSettings batchSettings,
        CancellationToken token)
    {
        if (chunk.Count == 0)
            return 0;

        token.ThrowIfCancellationRequested();

        await PumpUiAsync().ConfigureAwait(false);

        var result = await _markPrintService.PrintBatchAsync(new PrintBatchJobRequest
        {
            Renders = chunk.ToList(),
            PrinterName = batchSettings.PrinterName,
            PrintWithoutConfirmation = true,
            JobName = $"DoubleMark PDF ({chunk.Count})"
        }, token).ConfigureAwait(false);

        lock (_pdfBatchRecordsLock)
        {
            if (result.Success)
            {
                foreach (var page in pages)
                    SetPdfBatchRecordStatusUnlocked(page, PdfBatchPageStatus.Printed, null);
                return chunk.Count;
            }

            var error = result.Error ?? "Сбой печати";
            LoggingService.Error("PdfBatch", "Batch print failed: " + error);
            foreach (var page in pages)
                SetPdfBatchRecordStatusUnlocked(page, PdfBatchPageStatus.PrintFailed, "Сбой печати: " + error);
        }

        return 0;
    }

    private void ApplyPdfSummaryFromRecords(int totalPages, int selectedPages)
    {
        var printed = _pdfBatchRecords.Count(r => r.Status == PdfBatchPageStatus.Printed);
        var verified = _pdfBatchRecords.Count(r => r.Status == PdfBatchPageStatus.VerifiedOk);
        var decodeFailed = _pdfBatchRecords.Count(r => r.Status == PdfBatchPageStatus.DecodeFailed);
        var notReady = _pdfBatchRecords.Count(r => r.Status == PdfBatchPageStatus.NotReadyForPrint);
        var printFailed = _pdfBatchRecords.Count(r => r.Status == PdfBatchPageStatus.PrintFailed);

        if (printed > 0)
        {
            _pdfPrintSummary =
                $"PDF: {totalPages} стр. · выбрано {selectedPages} · напечатано {printed} · " +
                $"не прочитано {decodeFailed} · не готовы {notReady} · сбой печати {printFailed}";
        }
        else
        {
            _pdfPrintSummary =
                $"PDF: {totalPages} стр. · выбрано {selectedPages} · распознано {verified} · " +
                $"не прочитано {decodeFailed} · не готовы {notReady} · ошибок {decodeFailed + notReady + printFailed}";
        }
    }

    private static string BuildPdfPrintStatusMessage(int printed, int problems) =>
        $"Печать завершена: {printed} успешно, {problems} пропущено. " +
        "Откройте «Пропущенные» или историю, чтобы увидеть причины.";

    private async Task<PdfJobContext?> TryBeginPdfJobAsync()
    {
        if (_pdfPrintBusy)
            return null;

        var pdfPath = _selectedPdfPath ?? _pdfPrintView?.PdfPath ?? "";
        if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
        {
            _pdfPrintStatus = "Сначала выберите существующий PDF-файл.";
            SyncPdfPrintPageState();
            return null;
        }

        _selectedPdfPath = pdfPath;
        var jobGeneration = _pdfJobGeneration;
        _pdfBatchCts?.Cancel();
        _pdfBatchCts?.Dispose();
        _pdfBatchCts = new CancellationTokenSource();
        var token = _pdfBatchCts.Token;

        try
        {
            _pdfPrintBusy = true;
            _pdfPrintProgress = 0;
            _pdfPrintStatus = "Читаем PDF…";
            SyncPdfPrintPageState();

            var totalPages = await Task.Run(() => _pdfBatchDecodeService.GetPageCount(pdfPath), token);
            token.ThrowIfCancellationRequested();
            var pageRange = _pdfPrintView?.PageRange ?? string.Empty;
            if (!PageRangeParser.TryParse(pageRange, totalPages, out var pages, out var rangeError))
            {
                _pdfPrintStatus = rangeError ?? "Неверный диапазон страниц.";
                FinishPdfJob(jobGeneration);
                return null;
            }

            if (pages.Count > 5000)
            {
                _pdfPrintStatus = "Слишком много страниц: максимум 5000 за один запуск.";
                FinishPdfJob(jobGeneration);
                return null;
            }

            _pdfKnownTotalPages = totalPages;
            UpdatePdfPageCountSummary();

            return new PdfJobContext(pdfPath, pages, totalPages, token, jobGeneration);
        }
        catch (OperationCanceledException)
        {
            FinishPdfJob(jobGeneration);
            return null;
        }
        catch (Exception ex)
        {
            _pdfPrintStatus = "Не удалось открыть PDF: " + ex.Message;
            FinishPdfJob(jobGeneration);
            return null;
        }
    }

    private void FinishPdfJob(int jobGeneration)
    {
        if (jobGeneration != _pdfJobGeneration)
            return;

        _pdfPrintBusy = false;
        _pdfPrintProgress = 0;
        _pdfCanPrint = !string.IsNullOrWhiteSpace(_selectedPdfPath) && File.Exists(_selectedPdfPath);
        SyncPdfPrintPageState();
    }

    private async Task<int> FlushPdfPrintBatchAsync(
        List<MarkRenderResult> renderBatch,
        List<int> pageNumbers,
        PrintPipelineSettings batchSettings,
        CancellationToken token)
    {
        if (renderBatch.Count == 0)
            return 0;

        var chunk = renderBatch.ToList();
        var pages = pageNumbers.ToList();
        renderBatch.Clear();
        pageNumbers.Clear();

        return await FlushPdfPrintChunkAsync(chunk, pages, batchSettings, token);
    }

    private static async Task PumpUiAsync()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            await Task.Yield();
            return;
        }

        await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    private void ReportPdfProgress(int completed, int total, int pageNumber, string stage, bool force = false)
    {
        _pdfPrintProgress = total == 0 ? 0 : 100.0 * completed / total;
        _pdfPrintStatus = total == 0
            ? stage
            : $"{stage} · стр. {pageNumber} ({completed}/{total})";

        var now = DateTime.UtcNow;
        if (!force && completed < total && (now - _lastPdfUiUpdateUtc).TotalMilliseconds < 300)
            return;

        _lastPdfUiUpdateUtc = now;
        Dispatcher.BeginInvoke(SyncPdfPrintPageState, DispatcherPriority.Background);
    }

    private bool TryUpdatePreviewFromDecode(PdfPageDecodeResult decoded)
    {
        if (_cachedPdfPreview != null)
            return true;

        return UpdatePreviewFromDecode(decoded);
    }

    private bool UpdatePreviewFromDecode(PdfPageDecodeResult decoded)
    {
        if (decoded.ParseResult?.Code == null || string.IsNullOrEmpty(decoded.RawPayload))
            return false;

        var template = ResolveActiveTemplate();
        _cachedPdfPreview = TemplatePreviewRenderer.TryRender(
            template,
            _settings.LabelShowShipment,
            _settings.LabelShowOrder,
            _settings.LabelShipmentNumber,
            _settings.LabelOrderNumber,
            decoded.ParseResult,
            decoded.RawPayload,
            "PDF");
        return _cachedPdfPreview != null;
    }

    private void UpdatePreviewFromFirstValidPage(PdfBatchAnalyzeResult analyzeResult)
    {
        var first = analyzeResult.Results.FirstOrDefault(r => r.Success && r.ParseResult?.Code != null);
        if (first != null)
            UpdatePreviewFromDecode(first);
    }

    private void SyncPdfPrintPageState() =>
        _pdfPrintView?.UpdateState(BuildPdfPrintViewState());

    private PdfPrintViewState BuildPdfPrintViewState()
    {
        var printers = new[] { "По умолчанию" }
            .Concat(MarkPrintService.GetInstalledPrinters())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var template = ResolveActiveTemplate();
        var templates = _printTemplates.Select(t => t.Name).ToList();

        if (_cachedPdfPreview == null && !_pdfPrintBusy)
        {
            var firstValid = _lastPdfAnalyzeResult?.Results.FirstOrDefault(r => r.Success && r.ParseResult?.Code != null);
            if (firstValid != null)
                UpdatePreviewFromDecode(firstValid);
            else
                _cachedPdfPreview = TemplatePreviewRenderer.TryRender(template);
        }

        var problemCount = _pdfBatchRecords.Count(r => r.IsProblem);

        return new PdfPrintViewState
        {
            PdfPath = _selectedPdfPath ?? "",
            Printers = printers,
            SelectedPrinter = string.IsNullOrWhiteSpace(_settings.PrinterName) ? "По умолчанию" : _settings.PrinterName,
            Templates = templates,
            SelectedTemplate = template.Name,
            TemplateSize = $"{template.LabelWidthMm:0.#} × {template.LabelHeightMm:0.#} мм",
            DataMatrixSize = $"{template.DataMatrixWidthMm:0.#} × {template.DataMatrixHeightMm:0.#} мм",
            Status = _pdfPrintStatus,
            Summary = _pdfPrintSummary,
            PageCountText = _pdfPageCountText,
            PreviewImage = _cachedPdfPreview,
            IsBusy = _pdfPrintBusy,
            ProgressPercent = _pdfPrintProgress,
            CanPrint = _pdfCanPrint && !_pdfPrintBusy,
            HasBatchRecords = _pdfBatchRecords.Count > 0,
            ProblemCount = problemCount,
            TotalRecordCount = _pdfBatchRecords.Count,
            PageResults = BuildPdfPrintPageResults(),
            HistoryItems = BuildPdfHistoryItems()
        };
    }

    private IReadOnlyList<PdfPrintHistoryItem> BuildPdfHistoryItems() =>
        _pdfBatchHistoryService.ListRecent(20)
            .Select(summary =>
            {
                var localTime = summary.CompletedAt.ToLocalTime();
                var kind = summary.Kind == PdfBatchJobKind.Print ? "Печать" : "Проверка";
                var pdfMissing = !File.Exists(summary.PdfPath);
                var title = $"{localTime:dd.MM.yyyy HH:mm} · {kind} · {summary.PdfFileName}";
                var subtitle = pdfMissing
                    ? $"Напечатано {summary.PrintedCount}/{summary.SelectedPages} · проблемных {summary.ProblemCount} · файл не найден"
                    : $"Напечатано {summary.PrintedCount}/{summary.SelectedPages} · проблемных {summary.ProblemCount}";
                return new PdfPrintHistoryItem(summary.Id, title, subtitle, summary.ProblemCount, pdfMissing);
            })
            .ToList();

    private IReadOnlyList<PdfPrintPageResultItem> BuildPdfPrintPageResults()
    {
        if (_pdfBatchRecords.Count > 0)
        {
            return _pdfBatchRecords
                .OrderBy(r => r.PageNumber)
                .Select(r => new PdfPrintPageResultItem(
                    r.PageNumber,
                    !r.IsProblem,
                    r.Gtin,
                    r.Serial,
                    r.Reason,
                    r.StatusLabel))
                .ToList();
        }

        if (_lastPdfAnalyzeResult == null)
            return Array.Empty<PdfPrintPageResultItem>();

        return _lastPdfAnalyzeResult.Results
            .Select(r => new PdfPrintPageResultItem(
                r.PageNumber,
                r.Success,
                r.ParseResult?.Code?.Gtin,
                r.ParseResult?.Code?.Serial,
                r.Error))
            .ToList();
    }
}
