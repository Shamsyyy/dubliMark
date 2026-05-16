using System.Windows;
using System.Windows.Controls;

namespace DubliMark.Desktop.Views;

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
            PrintPreviewTemplateText.Text = state.SelectedTemplate ?? "ЧЗ";
            PrintPrinterSummaryText.Text = string.IsNullOrWhiteSpace(state.SelectedPrinter) ? "По умолчанию" : state.SelectedPrinter;
            PrintTemplateSummaryText.Text = state.SelectedTemplate ?? "—";

            PrintPreviewImage.Source = state.PreviewImage;
            PrintPreviewMock.Visibility = state.PreviewImage == null ? Visibility.Visible : Visibility.Collapsed;

            RenderRecentPrints(recentPrints);
        }
        finally
        {
            _updating = false;
        }
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
