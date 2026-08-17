using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DoubleMark.Desktop.Views;

public partial class PdfPrintView : UserControl
{
    private bool _updating;

    public event RoutedEventHandler? BrowsePdfRequested;
    public event RoutedEventHandler? AnalyzePdfRequested;
    public event RoutedEventHandler? PrintPdfRequested;
    public event RoutedEventHandler? CancelPdfRequested;
    public event RoutedEventHandler? OpenAllResultsRequested;
    public event RoutedEventHandler? OpenProblemsRequested;
    public event EventHandler<string>? OpenHistoryJobRequested;
    public event EventHandler<string>? OpenHistoryProblemsRequested;
    public event EventHandler<string?>? TemplateChanged;
    public event EventHandler<string?>? PrinterChanged;
    public event EventHandler? PageRangeChanged;

    public PdfPrintView() => InitializeComponent();

    public string PdfPath => PdfPathText.Text.Trim();
    public string PageRange => PageRangeText.Text.Trim();

    public void ClearPageRange()
    {
        _updating = true;
        try
        {
            PageRangeText.Text = string.Empty;
        }
        finally
        {
            _updating = false;
        }
    }

    public void UpdateState(PdfPrintViewState state)
    {
        _updating = true;
        try
        {
            if (!string.IsNullOrWhiteSpace(state.PdfPath))
                PdfPathText.Text = state.PdfPath;

            PrinterCombo.ItemsSource = state.Printers;
            PrinterCombo.SelectedItem = state.SelectedPrinter;
            if (PrinterCombo.SelectedIndex < 0 && state.Printers.Count > 0)
                PrinterCombo.SelectedIndex = 0;

            TemplateCombo.ItemsSource = state.Templates;
            TemplateCombo.SelectedItem = state.SelectedTemplate;
            if (TemplateCombo.SelectedIndex < 0 && state.Templates.Count > 0)
                TemplateCombo.SelectedIndex = 0;

            TemplateSizeText.Text = state.TemplateSize;
            DmSizeText.Text = "DataMatrix: " + state.DataMatrixSize;
            StatusText.Text = state.Status;
            SummaryText.Text = state.Summary;
            PageCountText.Text = string.IsNullOrWhiteSpace(state.PageCountText)
                ? "Количество страниц появится после выбора PDF."
                : state.PageCountText;

            PreviewImage.Source = state.PreviewImage;
            PreviewPlaceholder.Visibility = state.PreviewImage == null ? Visibility.Visible : Visibility.Collapsed;

            ProgressBar.Visibility = state.IsBusy ? Visibility.Visible : Visibility.Collapsed;
            ProgressBar.Value = state.ProgressPercent;
            AnalyzeButton.IsEnabled = !state.IsBusy;
            PrintButton.IsEnabled = !state.IsBusy && state.CanPrint;
            CancelButton.IsEnabled = state.IsBusy;
            CancelButton.Visibility = state.IsBusy ? Visibility.Visible : Visibility.Collapsed;

            OpenAllResultsButton.IsEnabled = !state.IsBusy && state.HasBatchRecords;
            OpenAllResultsButton.Content = state.HasBatchRecords
                ? $"Все коды ({state.TotalRecordCount})…"
                : "Все коды…";
            OpenProblemsButton.IsEnabled = !state.IsBusy && state.ProblemCount > 0;
            OpenProblemsButton.Content = state.ProblemCount > 0
                ? $"Пропущенные ({state.ProblemCount})…"
                : "Пропущенные…";

            RenderResults(state.PageResults);
            RenderHistory(state.HistoryItems);
        }
        finally
        {
            _updating = false;
        }
    }

    private void RenderResults(IReadOnlyList<PdfPrintPageResultItem> results)
    {
        ResultsPanel.Children.Clear();
        if (results.Count == 0)
        {
            ResultsPanel.Children.Add(Muted("После проверки или печати здесь появится краткий список. Полный — в отдельном окне."));
            return;
        }

        foreach (var item in results.Where(r => !r.Success).Take(6).Concat(results.Where(r => r.Success).Take(2)))
        {
            var brush = item.Success
                ? (Brush)FindResource("SuccessBrush")
                : (Brush)FindResource("DangerBrush");
            var text = item.Success
                ? $"Стр. {item.PageNumber}: GTIN {item.Gtin} · {item.Serial}"
                : string.IsNullOrWhiteSpace(item.StatusLabel)
                    ? $"Стр. {item.PageNumber}: {item.Error}"
                    : $"Стр. {item.PageNumber}: {item.StatusLabel} — {item.Error}";
            ResultsPanel.Children.Add(new Border
            {
                Style = (Style)FindResource("SoftSurface"),
                Margin = new Thickness(0, 0, 0, 8),
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = brush,
                    TextWrapping = TextWrapping.Wrap
                }
            });
        }

        var problemCount = results.Count(r => !r.Success);
        if (results.Count > 8 || problemCount > 0)
            ResultsPanel.Children.Add(Muted($"Всего {results.Count} стр. · проблемных {problemCount} — откройте «Все коды» или «Пропущенные»"));
    }

    private void RenderHistory(IReadOnlyList<PdfPrintHistoryItem> items)
    {
        HistoryPanel.Children.Clear();
        HistoryEmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var item in items.Take(12))
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var info = new StackPanel();
            info.Children.Add(new TextBlock
            {
                Text = item.Title,
                Foreground = (Brush)FindResource("TextBrush"),
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            info.Children.Add(new TextBlock
            {
                Text = item.Subtitle,
                Style = (Style)FindResource("MutedText"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });
            Grid.SetColumn(info, 0);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (item.ProblemCount > 0)
            {
                var problemsBtn = new Button
                {
                    Style = (Style)FindResource("SecondaryButton"),
                    Content = $"Проблемные ({item.ProblemCount})",
                    Tag = item.JobId,
                    Margin = new Thickness(0, 0, 6, 0)
                };
                problemsBtn.Click += (_, _) => OpenHistoryProblemsRequested?.Invoke(this, item.JobId);
                buttons.Children.Add(problemsBtn);
            }

            var openBtn = new Button
            {
                Style = (Style)FindResource("SecondaryButton"),
                Content = "Открыть",
                Tag = item.JobId
            };
            openBtn.Click += (_, _) => OpenHistoryJobRequested?.Invoke(this, item.JobId);
            buttons.Children.Add(openBtn);
            Grid.SetColumn(buttons, 1);

            grid.Children.Add(info);
            grid.Children.Add(buttons);

            HistoryPanel.Children.Add(new Border
            {
                Style = (Style)FindResource("SoftSurface"),
                Padding = new Thickness(10),
                Child = grid
            });
        }

        if (items.Count > 12)
            HistoryPanel.Children.Add(Muted($"… и ещё {items.Count - 12} записей в истории"));
    }

    private TextBlock Muted(string text) =>
        new()
        {
            Text = text,
            Style = (Style)FindResource("MutedText"),
            TextWrapping = TextWrapping.Wrap
        };

    private void OnBrowseClick(object sender, RoutedEventArgs e) =>
        BrowsePdfRequested?.Invoke(sender, e);

    private void OnAnalyzeClick(object sender, RoutedEventArgs e) =>
        AnalyzePdfRequested?.Invoke(sender, e);

    private void OnPrintClick(object sender, RoutedEventArgs e) =>
        PrintPdfRequested?.Invoke(sender, e);

    private void OnCancelClick(object sender, RoutedEventArgs e) =>
        CancelPdfRequested?.Invoke(sender, e);

    private void OnOpenAllResultsClick(object sender, RoutedEventArgs e) =>
        OpenAllResultsRequested?.Invoke(sender, e);

    private void OnOpenProblemsClick(object sender, RoutedEventArgs e) =>
        OpenProblemsRequested?.Invoke(sender, e);

    private void OnTemplateSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updating)
            TemplateChanged?.Invoke(this, TemplateCombo.SelectedItem as string);
    }

    private void OnPrinterSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updating)
            PrinterChanged?.Invoke(this, PrinterCombo.SelectedItem as string);
    }

    private void OnPageRangeTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_updating)
            PageRangeChanged?.Invoke(this, EventArgs.Empty);
    }
}
