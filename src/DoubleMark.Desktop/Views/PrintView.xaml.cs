using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DoubleMark.Desktop.Views;

public partial class PrintView : UserControl
{
    private bool _updating;

    public event RoutedEventHandler? PrintLastRequested;
    public event RoutedEventHandler? PrintSettingsRequested;
    public event RoutedEventHandler? OpenPrintFolderRequested;
    public event RoutedEventHandler? TestPrintRequested;
    public event EventHandler<bool>? AutoPrintChanged;
    public event EventHandler<string?>? PrinterChanged;
    public event EventHandler<string?>? TemplateChanged;
    public event EventHandler<int>? CopiesChanged;

    public PrintView() => InitializeComponent();

    public void UpdateState(PrintViewState state, IReadOnlyList<ScanHistoryItem> recentPrints)
    {
        _updating = true;
        try
        {
            PrintAutoToggle.IsChecked = state.AutoPrintEnabled;
            PrintAutoStateText.Text = state.AutoPrintEnabled ? "Вкл." : "Выкл.";
            PrintAutoSummaryText.Text = state.AutoPrintEnabled ? "Включена" : "Выключена";

            PrinterCombo.ItemsSource = state.Printers;
            PrinterCombo.SelectedItem = state.SelectedPrinter;
            if (PrinterCombo.SelectedIndex < 0 && state.Printers.Count > 0)
                PrinterCombo.SelectedIndex = 0;

            TemplateCombo.ItemsSource = state.Templates;
            TemplateCombo.SelectedItem = state.SelectedTemplate;
            if (TemplateCombo.SelectedIndex < 0 && state.Templates.Count > 0)
                TemplateCombo.SelectedIndex = 0;

            CopiesCombo.SelectedIndex = Math.Clamp(state.Copies, 1, 5) - 1;

            PrintStatusText.Text = string.IsNullOrWhiteSpace(state.LastPrintStatus) ? "—" : state.LastPrintStatus;
            PrintQueueText.Text = state.QueueStatus;
            PrintFolderText.Text = state.PrintFolder;
            PrintTemplateSizeText.Text = state.TemplateSize;
            PrintDmSizeText.Text = "DataMatrix: " + state.DataMatrixSize;
            PrintPrinterSummaryText.Text = string.IsNullOrWhiteSpace(state.SelectedPrinter) ? "По умолчанию" : state.SelectedPrinter;
            PrintTemplateSummaryText.Text = state.SelectedTemplate ?? "—";

            PrintPreviewImage.Source = state.PreviewImage;
            PrintPreviewMock.Visibility = state.PreviewImage == null ? Visibility.Visible : Visibility.Collapsed;
            if (state.PreviewImage == null)
                DrawMockPreview(state);

            RenderRecentPrints(recentPrints);
        }
        finally
        {
            _updating = false;
        }
    }

    private void DrawMockPreview(PrintViewState state)
    {
        PrintPreviewMockCanvas.Children.Clear();
        if (state.LabelWidthMm <= 0 || state.LabelHeightMm <= 0)
            return;

        const double maxWidth = 288;
        const double maxHeight = 188;
        var scale = Math.Min(maxWidth / state.LabelWidthMm, maxHeight / state.LabelHeightMm);
        if (double.IsInfinity(scale) || scale <= 0)
            scale = 1;

        var labelWidth = state.LabelWidthMm * scale;
        var labelHeight = state.LabelHeightMm * scale;
        var offsetX = (maxWidth - labelWidth) / 2;
        var offsetY = (maxHeight - labelHeight) / 2;

        var label = new Rectangle
        {
            Width = labelWidth,
            Height = labelHeight,
            RadiusX = 8,
            RadiusY = 8,
            Fill = Brushes.White,
            Stroke = (Brush)new BrushConverter().ConvertFrom("#D6DEE8")!,
            StrokeThickness = 1
        };
        Canvas.SetLeft(label, offsetX);
        Canvas.SetTop(label, offsetY);
        PrintPreviewMockCanvas.Children.Add(label);

        var dmSize = Math.Min(state.DataMatrixWidthMm * scale, state.DataMatrixHeightMm * scale);
        var dm = BuildMatrixCanvas(dmSize);
        Canvas.SetLeft(dm, offsetX + state.DataMatrixXmm * scale);
        Canvas.SetTop(dm, offsetY + state.DataMatrixYmm * scale);
        PrintPreviewMockCanvas.Children.Add(dm);

        var templateName = new TextBlock
        {
            Text = state.SelectedTemplate ?? "ЧЗ",
            Foreground = (Brush)new BrushConverter().ConvertFrom("#111820")!,
            FontWeight = FontWeights.Bold,
            FontSize = 11,
            Width = Math.Max(86, labelWidth - state.DataMatrixXmm * scale - dmSize - 10),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Canvas.SetLeft(templateName, Math.Min(offsetX + labelWidth - templateName.Width - 6, offsetX + state.DataMatrixXmm * scale + dmSize + 8));
        Canvas.SetTop(templateName, offsetY + Math.Max(4, state.DataMatrixYmm * scale));
        PrintPreviewMockCanvas.Children.Add(templateName);
    }

    private static Canvas BuildMatrixCanvas(double size)
    {
        var canvas = new Canvas
        {
            Width = size,
            Height = size,
            Background = Brushes.White
        };

        var dark = (Brush)new BrushConverter().ConvertFrom("#111820")!;
        const int cells = 10;
        var cell = size / cells;
        for (var y = 0; y < cells; y++)
        {
            for (var x = 0; x < cells; x++)
            {
                var finder = x == 0 || y == cells - 1 || (x % 2 == 0 && y == 0) || (x == cells - 1 && y % 2 == 0);
                var data = ((x * 5 + y * 7 + x * y) % 4) == 0;
                if (!finder && !data)
                    continue;

                var rect = new Rectangle
                {
                    Width = Math.Ceiling(cell),
                    Height = Math.Ceiling(cell),
                    Fill = dark
                };
                Canvas.SetLeft(rect, x * cell);
                Canvas.SetTop(rect, y * cell);
                canvas.Children.Add(rect);
            }
        }

        return canvas;
    }

    private void RenderRecentPrints(IReadOnlyList<ScanHistoryItem> recentPrints)
    {
        RecentPrintsPanel.Children.Clear();
        var printed = recentPrints
            .Where(i => !string.IsNullOrWhiteSpace(i.PrintStatus) && i.PrintStatus != "—")
            .Take(5)
            .ToList();

        if (printed.Count == 0)
        {
            RecentPrintsPanel.Children.Add(new TextBlock
            {
                Text = "Печатей в текущей сессии пока нет",
                Style = (Style)FindResource("MutedText")
            });
            return;
        }

        foreach (var item in printed)
        {
            RecentPrintsPanel.Children.Add(new Border
            {
                Style = (Style)FindResource("SoftSurface"),
                Margin = new Thickness(0, 0, 0, 8),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = item.Timestamp.ToString("dd.MM.yyyy HH:mm:ss") + " · " + item.Status,
                            Foreground = (System.Windows.Media.Brush)FindResource("TextBrush"),
                            FontWeight = FontWeights.SemiBold
                        },
                        new TextBlock
                        {
                            Text = item.Template + " · " + item.Printer,
                            Style = (Style)FindResource("MutedText"),
                            TextTrimming = TextTrimming.CharacterEllipsis
                        }
                    }
                }
            });
        }
    }

    private void OnPrintLastProxyClick(object sender, RoutedEventArgs e) =>
        PrintLastRequested?.Invoke(sender, e);

    private void OnPrintSettingsProxyClick(object sender, RoutedEventArgs e) =>
        PrintSettingsRequested?.Invoke(sender, e);

    private void OnOpenPrintFolderProxyClick(object sender, RoutedEventArgs e) =>
        OpenPrintFolderRequested?.Invoke(sender, e);

    private void OnTestPrintProxyClick(object sender, RoutedEventArgs e) =>
        TestPrintRequested?.Invoke(sender, e);

    private void OnAutoPrintToggleChanged(object sender, RoutedEventArgs e)
    {
        if (!_updating)
            AutoPrintChanged?.Invoke(this, PrintAutoToggle.IsChecked == true);
    }

    private void OnPrinterSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updating)
            PrinterChanged?.Invoke(this, PrinterCombo.SelectedItem as string);
    }

    private void OnTemplateSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updating)
            TemplateChanged?.Invoke(this, TemplateCombo.SelectedItem as string);
    }

    private void OnCopiesSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || CopiesCombo.SelectedIndex < 0)
            return;

        CopiesChanged?.Invoke(this, CopiesCombo.SelectedIndex + 1);
    }
}
