using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using DoubleMark.Desktop.Services;

namespace DoubleMark.Desktop;

public enum PdfBatchResultsMode
{
    All,
    ProblemsOnly
}

public sealed class PdfBatchRetryRequest
{
    public required string PdfPath { get; init; }
    public string? HistoryJobId { get; init; }
    public required IReadOnlyList<int> PageNumbers { get; init; }
    public bool PrintAfter { get; init; }
    public IProgress<PdfBatchProgress>? Progress { get; init; }
    public CancellationToken CancellationToken { get; init; }
}

public sealed class PdfBatchRetryResult
{
    public int Rechecked { get; init; }
    public int Printed { get; init; }
    public int StillProblem { get; init; }
    public IReadOnlyList<PdfBatchPageRecord> Records { get; init; } = Array.Empty<PdfBatchPageRecord>();
}

public sealed class PdfBatchResultsRow : INotifyPropertyChanged
{
    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public int PageNumber { get; init; }
    public string Gtin { get; init; } = "—";
    public string Serial { get; init; } = "—";
    public string Status { get; init; } = "—";
    public string Reason { get; init; } = "—";
    public bool IsProblem { get; init; }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class PdfBatchResultsWindow : Window
{
    private readonly PdfBatchResultsMode _mode;
    private readonly Func<PdfBatchRetryRequest, Task<PdfBatchRetryResult>>? _retryAsync;
    private readonly ObservableCollection<PdfBatchResultsRow> _rows = new();
    private IReadOnlyList<PdfBatchPageRecord> _records = Array.Empty<PdfBatchPageRecord>();
    private CancellationTokenSource? _retryCts;
    private readonly HashSet<int> _problemSessionPages = new();
    private bool _busy;
    private string? _historyJobId;
    private readonly bool _readOnly;

    public PdfBatchResultsWindow(
        PdfBatchResultsMode mode,
        IReadOnlyList<PdfBatchPageRecord> records,
        Func<PdfBatchRetryRequest, Task<PdfBatchRetryResult>>? retryAsync,
        string pdfPath,
        string? historyJobId = null,
        string? subtitle = null,
        bool readOnly = false)
    {
        InitializeComponent();
        _mode = mode;
        _retryAsync = retryAsync;
        _historyJobId = historyJobId;
        _readOnly = readOnly;
        Tag = pdfPath;
        ResultsGrid.ItemsSource = _rows;
        if (!string.IsNullOrWhiteSpace(subtitle))
            HintText.Text = subtitle;
        Reload(records);
    }

    private void Reload(IReadOnlyList<PdfBatchPageRecord> records)
    {
        _records = records;

        if (_mode == PdfBatchResultsMode.ProblemsOnly)
        {
            foreach (var record in records.Where(r => r.IsProblem))
                _problemSessionPages.Add(record.PageNumber);
        }

        _rows.Clear();
        foreach (var record in records.OrderBy(r => r.PageNumber))
        {
            if (_mode == PdfBatchResultsMode.ProblemsOnly && !_problemSessionPages.Contains(record.PageNumber))
                continue;

            _rows.Add(ToRow(record, record.IsProblem));
        }

        UpdateChrome();
    }

    private void UpdateChrome()
    {
        var printed = _records.Count(r => r.Status == PdfBatchPageStatus.Printed);
        if (_mode == PdfBatchResultsMode.ProblemsOnly)
        {
            var sessionTotal = _problemSessionPages.Count;
            var stillProblems = _records.Count(r => r.IsProblem && _problemSessionPages.Contains(r.PageNumber));
            var resolved = Math.Max(0, sessionTotal - stillProblems);
            TitleText.Text = sessionTotal == 0 ? "Пропущенные" : $"Пропущенные ({sessionTotal})";
            HeaderText.Text = sessionTotal == 0
                ? "Проблемных страниц нет."
                : stillProblems == 0
                    ? $"Все {sessionTotal} страниц распознаны. Можно напечатать выбранные."
                    : $"В списке {sessionTotal} стр. · исправлено {resolved} · осталось проблемных {stillProblems}. Перепроверка использует улучшенное чтение DataMatrix.";
            var showProblemActions = !_readOnly && sessionTotal > 0;
            SelectProblemsButton.Visibility = showProblemActions ? Visibility.Visible : Visibility.Collapsed;
            RecheckSelectedButton.IsEnabled = showProblemActions && !_busy;
            RecheckAllButton.IsEnabled = stillProblems > 0 && !_busy && !_readOnly;
            PrintSelectedButton.Visibility = _readOnly ? Visibility.Collapsed : Visibility.Visible;
            PrintSelectedButton.IsEnabled = showProblemActions && !_busy;
            CancelButton.Visibility = _busy ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        var problems = _records.Count(r => r.IsProblem);
        TitleText.Text = $"Все коды ({_records.Count})";
        HeaderText.Text = $"Всего: {_records.Count} · напечатано {printed} · проблемных {problems}";

        var showProblemActionsAll = !_readOnly && problems > 0;
        SelectProblemsButton.Visibility = showProblemActionsAll ? Visibility.Visible : Visibility.Collapsed;
        RecheckSelectedButton.IsEnabled = showProblemActionsAll && !_busy;
        RecheckAllButton.IsEnabled = false;
        PrintSelectedButton.Visibility = _readOnly ? Visibility.Collapsed : Visibility.Visible;
        PrintSelectedButton.IsEnabled = !_readOnly && !_busy;
        CancelButton.Visibility = _busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string FormatReason(PdfBatchPageRecord record)
    {
        if (!record.IsProblem)
        {
            return record.Status switch
            {
                PdfBatchPageStatus.VerifiedOk => "Код распознан корректно",
                PdfBatchPageStatus.Printed => "Напечатано",
                _ => string.IsNullOrWhiteSpace(record.Reason) ? "—" : record.Reason
            };
        }

        return string.IsNullOrWhiteSpace(record.Reason) ? "—" : record.Reason;
    }

    private static PdfBatchResultsRow ToRow(PdfBatchPageRecord record, bool selected) =>
        new()
        {
            PageNumber = record.PageNumber,
            Gtin = record.Gtin ?? "—",
            Serial = record.Serial ?? "—",
            Status = record.StatusLabel,
            Reason = FormatReason(record),
            IsProblem = record.IsProblem,
            IsSelected = selected
        };

    private void OnSelectProblemsClick(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows)
            row.IsSelected = row.IsProblem;
    }

    private async void OnRecheckSelectedClick(object sender, RoutedEventArgs e) =>
        await RunRetryAsync(GetSelectedPages(), printAfter: false);

    private async void OnRecheckAllClick(object sender, RoutedEventArgs e) =>
        await RunRetryAsync(_rows.Where(r => r.IsProblem).Select(r => r.PageNumber).ToList(), printAfter: false);

    private async void OnPrintSelectedClick(object sender, RoutedEventArgs e) =>
        await RunRetryAsync(GetSelectedPages(), printAfter: true);

    private List<int> GetSelectedPages() =>
        _rows.Where(r => r.IsSelected).Select(r => r.PageNumber).Distinct().OrderBy(n => n).ToList();

    private async Task RunRetryAsync(IReadOnlyList<int> pages, bool printAfter)
    {
        if (_busy || pages.Count == 0)
        {
            MessageBox.Show(this, "Выберите хотя бы одну страницу.", "PDF-пакет", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var pdfPath = Tag as string ?? "";
        if (string.IsNullOrWhiteSpace(pdfPath))
        {
            MessageBox.Show(this, "PDF-файл не указан.", "PDF-пакет", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _busy = true;
        _retryCts = new CancellationTokenSource();
        ProgressPanel.Visibility = Visibility.Visible;
        ProgressBar.Value = 0;
        ProgressStatusText.Text = printAfter ? "Перепроверка и печать…" : "Перепроверка…";
        UpdateChrome();

        var progress = new Progress<PdfBatchProgress>(report =>
        {
            ProgressBar.Value = report.TotalPages == 0 ? 0 : 100.0 * report.CompletedCount / report.TotalPages;
            ProgressStatusText.Text = $"{report.Stage} · стр. {report.CurrentPage} ({report.CompletedCount}/{report.TotalPages})";
        });

        try
        {
            if (_retryAsync == null)
                throw new InvalidOperationException("PDF-файл недоступен для перепечатки.");

            var result = await _retryAsync(new PdfBatchRetryRequest
            {
                PdfPath = pdfPath,
                HistoryJobId = _historyJobId,
                PageNumbers = pages,
                PrintAfter = printAfter,
                Progress = progress,
                CancellationToken = _retryCts.Token
            }).ConfigureAwait(true);

            Reload(result.Records);
            var action = printAfter ? "Обработано" : "Перепроверено";
            MessageBox.Show(this,
                $"{action}: {result.Rechecked} стр.\nНапечатано: {result.Printed}\nОсталось проблемных: {result.StillProblem}",
                "PDF-пакет",
                MessageBoxButton.OK,
                printAfter ? MessageBoxImage.Information : MessageBoxImage.None);
        }
        catch (OperationCanceledException)
        {
            MessageBox.Show(this, "Операция отменена.", "PDF-пакет", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "PDF-пакет", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _busy = false;
            _retryCts?.Dispose();
            _retryCts = null;
            ProgressPanel.Visibility = Visibility.Collapsed;
            UpdateChrome();
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => _retryCts?.Cancel();

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            if (MessageBox.Show(this, "Операция ещё выполняется. Отменить и закрыть?",
                    "PDF-пакет", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            _retryCts?.Cancel();
        }

        Close();
    }
}
