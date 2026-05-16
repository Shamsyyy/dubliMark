using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DubliMark.Core.Models;
using DubliMark.Core.Parsing;
using DubliMark.Core.Print;
using DubliMark.Desktop.Services;

namespace DubliMark.Desktop;

public partial class MainWindow
{
    private readonly PrintTemplateService _printTemplateService = new();
    private PrintPipelineService _printPipeline = null!;
    private IReadOnlyList<PrintTemplate> _printTemplates = Array.Empty<PrintTemplate>();
    private LastSuccessfulScan? _lastSuccessfulScan;
    private string? _lastScanFolder;
    private string? _lastScanCopyText;

    private void InitializePrintServices()
    {
        _printPipeline = new PrintPipelineService(
            new MarkRenderService(),
            new PrintExportService(),
            new MarkPrintService(this));
    }


    private async Task<PrintPipelineResult?> ProcessPrintAfterScanAsync(
        ParseResult result,
        string raw,
        string source,
        bool forcePrint,
        bool allowDuplicate)
    {
        if (!result.IsValid || result.Code == null)
            return null;

        _lastSuccessfulScan = new LastSuccessfulScan(raw, result, source);
        var settings = _settings.ToPrintPipelineSettings();
        if (!forcePrint && !settings.AutoPrintEnabled)
        {
            LastPrintStatusText.Text = "Автопечать выключена. Можно напечатать последний ЧЗ вручную.";
            return null;
        }

        var template = ResolveActiveTemplate();
        LastPrintStatusText.Text = forcePrint ? "Печать..." : "Автопечать...";
        var print = await _printPipeline.ProcessAsync(new PrintPipelineRequest
        {
            RawPayload = raw,
            ParseResult = result,
            Source = source,
            Template = template,
            Settings = settings,
            ForcePrint = forcePrint,
            AllowDuplicate = allowDuplicate
        });

        UpdatePrintStatus(print);
        return print;
    }

    private PrintTemplate ResolveActiveTemplate()
    {
        if (_printTemplates.Count == 0)
            _printTemplates = _printTemplateService.LoadOrCreateDefaults();

        var template = _printTemplates.FirstOrDefault(t =>
            string.Equals(t.Name, _settings.DefaultPrintTemplateName, StringComparison.OrdinalIgnoreCase));
        return template ?? _printTemplates[0];
    }

    private void UpdatePrintStatus(PrintPipelineResult? result)
    {
        if (result == null)
            return;

        if (result.BlockedDuplicate)
        {
            LastPrintStatusText.Text = result.Error ?? "";
            ShowToast("Повторная печать заблокирована", ToastKind.Warning);
            return;
        }

        if (result.Printed)
        {
            var folder = result.Export?.DirectoryPath;
            LastPrintStatusText.Text = string.IsNullOrWhiteSpace(folder)
                ? "Напечатано"
                : "Напечатано и сохранено: " + folder;
            ShowToast("Напечатано", ToastKind.Success);
            return;
        }

        LastPrintStatusText.Text = string.IsNullOrWhiteSpace(result.Error)
            ? "Печать не выполнялась"
            : "Ошибка печати: " + result.Error;
        if (!string.IsNullOrWhiteSpace(result.Error))
            ShowToast("Ошибка печати: " + result.Error, ToastKind.Error);
    }

    private static void AddPrintResult(StackPanel sp, PrintPipelineResult? printResult)
    {
        if (printResult == null)
            return;

        if (printResult.BlockedDuplicate)
        {
            sp.Children.Add(Field("Печать", printResult.Error ?? "Повторная печать заблокирована", small: true));
            return;
        }

        if (printResult.Printed)
        {
            sp.Children.Add(Field("Печать", "Напечатано", small: true));
            if (printResult.Export?.DirectoryPath != null)
                sp.Children.Add(Field("Файлы печати", printResult.Export.DirectoryPath, small: true));
            return;
        }

        if (!string.IsNullOrWhiteSpace(printResult.Error))
            sp.Children.Add(Field("Ошибка печати", printResult.Error, small: true));
    }

    private void RefreshPrintSettingsUi()
    {
        if (_printTemplates.Count == 0)
            _printTemplates = _printTemplateService.LoadOrCreateDefaults();

        AutoPrintQuickToggle.IsChecked = _settings.AutoPrintEnabled;
        AutoPrintStatusText.Text = _settings.AutoPrintEnabled ? "Вкл." : "Выкл.";
        PrintTemplateText.Text = ResolveActiveTemplate().Name;
        PrintPrinterText.Text = string.IsNullOrWhiteSpace(_settings.PrinterName) ? "По умолчанию" : _settings.PrinterName;
        PrintCopiesText.Text = Math.Max(1, _settings.PrintCopies).ToString();
        TemplateLargeText.Text = _printTemplates.FirstOrDefault(t => t.LabelWidthMm >= 40)?.Name ?? "ЧЗ 40×30 мм";
        TemplateSmallText.Text = _printTemplates.FirstOrDefault(t => t.LabelWidthMm <= 30)?.Name ?? "ЧЗ 30×20 мм";
    }

    private void OnAutoPrintQuickToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings)
            return;

        _settings.AutoPrintEnabled = AutoPrintQuickToggle.IsChecked == true;
        _settings.Save();
        RefreshPrintSettingsUi();
    }

    private async void OnPrintLastClick(object sender, RoutedEventArgs e)
    {
        if (_lastSuccessfulScan == null)
        {
            LastPrintStatusText.Text = "Нет успешного ЧЗ для печати.";
            return;
        }

        var confirm = MessageBox.Show(
            "Напечатать последний ЧЗ повторно?",
            "Подтверждение печати",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        var result = await ProcessPrintAfterScanAsync(
            _lastSuccessfulScan.ParseResult,
            _lastSuccessfulScan.Raw,
            _lastSuccessfulScan.Source,
            forcePrint: true,
            allowDuplicate: true);
        UpdatePrintStatus(result);
    }

    private void OnPrintSettingsClick(object sender, RoutedEventArgs e)
    {
        var window = new PrintSettingsWindow(_settings, _printTemplates) { Owner = this };
        if (window.ShowDialog() == true && window.ResultSettings != null)
        {
            _settings = window.ResultSettings;
            _settings.Save();
            RefreshPrintSettingsUi();
        }
    }

    private void OnPrintTemplatesClick(object sender, RoutedEventArgs e)
    {
        var window = new PrintTemplatesWindow(_printTemplates) { Owner = this };
        if (window.ShowDialog() == true)
        {
            _printTemplates = window.Templates;
            _printTemplateService.SaveTemplates(_printTemplates);
            if (!_printTemplates.Any(t => string.Equals(t.Name, _settings.DefaultPrintTemplateName, StringComparison.OrdinalIgnoreCase)))
                _settings.DefaultPrintTemplateName = _printTemplates.FirstOrDefault()?.Name ?? "ЧЗ 30x20 мм";
            _settings.Save();
            RefreshPrintSettingsUi();
        }
    }

    private void OnOpenPrintFolderClick(object sender, RoutedEventArgs e) =>
        OpenFolder(_settings.EffectivePrintDirectory);

    private void OnOpenExportRootFolderClick(object sender, RoutedEventArgs e) =>
        OpenFolder(_settings.EffectiveExportDirectory);

    private void OnOpenLastScanFolderClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_lastScanFolder))
            OpenFolder(_lastScanFolder);
    }

    private void OnCopyLastScanClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_lastScanCopyText))
            Clipboard.SetText(_lastScanCopyText);
    }

    private static void OpenFolder(string folder)
    {
        if (!Directory.Exists(folder))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "\"" + folder + "\"",
            UseShellExecute = true
        });
    }

    private sealed record LastSuccessfulScan(string Raw, ParseResult ParseResult, string Source);
}
