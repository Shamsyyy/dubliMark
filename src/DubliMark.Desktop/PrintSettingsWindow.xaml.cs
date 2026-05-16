using System.Diagnostics;
using System.IO;
using System.Windows;
using DubliMark.Core.Parsing;
using DubliMark.Core.Print;
using DubliMark.Desktop.Services;
using DubliMark.Desktop.Settings;
using Microsoft.Win32;

namespace DubliMark.Desktop;

public partial class PrintSettingsWindow : Window
{
    private readonly AppSettings _baseSettings;
    private readonly IReadOnlyList<PrintTemplate> _templates;

    public AppSettings? ResultSettings { get; private set; }

    public PrintSettingsWindow(AppSettings settings, IReadOnlyList<PrintTemplate> templates)
    {
        _baseSettings = settings;
        _templates = templates;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PrinterCombo.ItemsSource = MarkPrintService.GetInstalledPrinters();
        TemplateCombo.ItemsSource = _templates.Select(t => t.Name).ToList();

        AutoPrintCheck.IsChecked = _baseSettings.AutoPrintEnabled;
        SilentPrintCheck.IsChecked = _baseSettings.PrintWithoutConfirmation;
        SaveBeforePrintCheck.IsChecked = _baseSettings.SavePrintFileBeforePrint;
        PrinterCombo.SelectedItem = _baseSettings.PrinterName;
        TemplateCombo.SelectedItem = _baseSettings.DefaultPrintTemplateName ?? _templates.FirstOrDefault()?.Name;
        CopiesText.Text = _baseSettings.PrintCopies.ToString();
        DelayText.Text = _baseSettings.PrintDelayMs.ToString();
        DuplicateText.Text = _baseSettings.DuplicatePrintBlockSeconds.ToString();
        PrintFolderText.Text = _baseSettings.EffectivePrintDirectory;
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Выберите папку сохранения печатных этикеток",
            InitialDirectory = Directory.Exists(PrintFolderText.Text)
                ? PrintFolderText.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        };

        if (dlg.ShowDialog() == true)
            PrintFolderText.Text = dlg.FolderName;
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        var folder = PrintFolderText.Text;
        if (Directory.Exists(folder))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "\"" + folder + "\"",
                UseShellExecute = true
            });
        }
    }

    private async void OnTestPrintClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = BuildSettings();
            var template = _templates.FirstOrDefault(t => t.Name == settings.DefaultPrintTemplateName)
                           ?? _templates.First();
            var parser = new Gs1Parser();
            var raw = $"010460000000000221TESTSERIAL{(char)0x1D}91TKEY{(char)0x1D}92TESTCRYPTO";
            var parse = parser.Parse(raw);
            var pipeline = new PrintPipelineService(
                new MarkRenderService(),
                new PrintExportService(),
                new MarkPrintService(this));
            var result = await pipeline.ProcessAsync(new PrintPipelineRequest
            {
                RawPayload = raw,
                ParseResult = parse,
                Source = "Manual",
                Template = template,
                Settings = settings.ToPrintPipelineSettings() with { AutoPrintEnabled = true },
                ForcePrint = true,
                AllowDuplicate = true
            });

            StatusText.Text = result.Printed ? "Тестовая печать отправлена." : "Тестовая печать: " + result.Error;
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        ResultSettings = BuildSettings();
        DialogResult = true;
        Close();
    }

    private AppSettings BuildSettings()
    {
        var settings = new AppSettings
        {
            ComPort = _baseSettings.ComPort,
            ScannerDevicePath = _baseSettings.ScannerDevicePath,
            ScannerMode = _baseSettings.ScannerMode,
            AutoSaveExports = _baseSettings.AutoSaveExports,
            ExportDirectory = _baseSettings.ExportDirectory,
            ScannerGsMappingMode = _baseSettings.ScannerGsMappingMode,
            ScannerVisibleGsChar = _baseSettings.ScannerVisibleGsChar,
            ScannerCustomGsVkey = _baseSettings.ScannerCustomGsVkey,
            ScannerCustomGsMakeCode = _baseSettings.ScannerCustomGsMakeCode,
            ScannerCustomGsRequiresCtrl = _baseSettings.ScannerCustomGsRequiresCtrl,
            ScannerCustomGsRequiresShift = _baseSettings.ScannerCustomGsRequiresShift,
            ScannerCustomGsRequiresAlt = _baseSettings.ScannerCustomGsRequiresAlt,
            AutoPrintEnabled = AutoPrintCheck.IsChecked == true,
            PrinterName = PrinterCombo.SelectedItem as string,
            PrintCopies = ParsePositive(CopiesText.Text, 1),
            PrintWithoutConfirmation = SilentPrintCheck.IsChecked == true,
            PrintDelayMs = ParseNonNegative(DelayText.Text),
            DuplicatePrintBlockSeconds = ParseNonNegative(DuplicateText.Text, 5),
            SavePrintFileBeforePrint = SaveBeforePrintCheck.IsChecked == true,
            PrintDirectory = string.IsNullOrWhiteSpace(PrintFolderText.Text) ? null : PrintFolderText.Text,
            DefaultPrintTemplateName = TemplateCombo.SelectedItem as string
        };

        return settings;
    }

    private static int ParsePositive(string text, int fallback) =>
        int.TryParse(text, out var value) && value > 0 ? value : fallback;

    private static int ParseNonNegative(string text, int fallback = 0) =>
        int.TryParse(text, out var value) && value >= 0 ? value : fallback;

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
