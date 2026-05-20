using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DoubleMark.Core.Models;
using DoubleMark.Core.Print;
using DoubleMark.Desktop.Services;
using DoubleMark.Desktop.Settings;

namespace DoubleMark.Desktop;

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
        var isAuto = !forcePrint && settings.AutoPrintEnabled;

        if (!forcePrint && !settings.AutoPrintEnabled)
        {
            LastPrintStatusText.Text = "Ручная печать: нажмите «Печать последнего ЧЗ».";
            return null;
        }

        if (!await EnsureSubscriptionForFeatureAsync(forcePrint ? "Печать" : "Автопечать"))
        {
            LastPrintStatusText.Text = "Для печати нужна активная подписка DoubleMark.";
            return null;
        }

        if (!ScanDiagnosticsHelper.IsReadyForPrint(result, out var printReason))
        {
            var msg = "Печать невозможна: " + printReason;
            LoggingService.Warn(isAuto ? "Print.Auto" : "Print.Manual", msg);
            LastPrintStatusText.Text = msg;
            if (isAuto)
                ShowToast(msg, ToastKind.Warning);
            return null;
        }

        var template = ResolveActiveTemplate();
        if (string.IsNullOrWhiteSpace(template.Name))
        {
            const string warn = "Автопечать не выполнена: не выбран шаблон.";
            LoggingService.Warn("Print.Auto", warn);
            LastPrintStatusText.Text = warn;
            if (isAuto)
                ShowToast(warn, ToastKind.Warning);
            return null;
        }

        var printerLabel = string.IsNullOrWhiteSpace(_settings.PrinterName) ? "default" : _settings.PrinterName;
        LoggingService.Info(isAuto ? "Print.Auto" : "Print.Manual",
            $"Preparing template={template.Name} ({template.LabelWidthMm:0.#}x{template.LabelHeightMm:0.#}mm) " +
            $"printer={printerLabel} mode={_settings.PrintMode}");

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

        if (print.BlockedDuplicate)
            LoggingService.Info("Print.Auto", "Duplicate scan ignored");

        if (print.Printed)
            LoggingService.Info(isAuto ? "Print.Auto" : "Print.Manual",
                $"Sent to printer: {printerLabel} template={template.Name}");

        if (!print.Printed && !string.IsNullOrWhiteSpace(print.Error))
            LoggingService.Error(isAuto ? "Print.Auto" : "Print.Manual", "Print failed: " + print.Error);

        UpdatePrintStatus(print, isAuto);
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

    private PrintTemplate EnsureActiveTemplateSelection()
    {
        var template = ResolveActiveTemplate();
        if (!string.Equals(_settings.DefaultPrintTemplateName, template.Name, StringComparison.OrdinalIgnoreCase))
        {
            _settings.DefaultPrintTemplateName = template.Name;
            _settings.Save();
        }

        return template;
    }

    private void SetActivePrintTemplate(string? templateName, bool showToast = true)
    {
        if (string.IsNullOrWhiteSpace(templateName))
            return;

        if (_printTemplates.Count == 0)
            _printTemplates = _printTemplateService.LoadOrCreateDefaults();

        var template = _printTemplates.FirstOrDefault(t =>
            string.Equals(t.Name, templateName, StringComparison.OrdinalIgnoreCase));
        if (template == null)
            return;

        _settings.DefaultPrintTemplateName = template.Name;
        _settings.Save();
        RefreshPreviewForActiveTemplate();
        RefreshPrintSettingsUi();
        SyncConnectedViews();

        if (showToast)
            ShowToast("Шаблон печати выбран: " + template.Name, ToastKind.Success);
    }

    private void UpdatePrintStatus(PrintPipelineResult? result, bool isAutoPrint = false)
    {
        if (result == null)
            return;

        if (result.BlockedDuplicate)
        {
            LastPrintStatusText.Text = result.Error ?? "";
            ShowToast("Повторный скан проигнорирован", ToastKind.Warning);
            SyncPrintPageState();
            return;
        }

        if (result.Printed)
        {
            var folder = result.Export?.DirectoryPath;
            LastPrintStatusText.Text = string.IsNullOrWhiteSpace(folder)
                ? "Напечатано"
                : "Напечатано и сохранено: " + folder;
            ShowToast(isAutoPrint ? "Этикетка отправлена на печать" : "Напечатано", ToastKind.Success);
            SyncPrintPageState();
            return;
        }

        LastPrintStatusText.Text = string.IsNullOrWhiteSpace(result.Error)
            ? "Печать не выполнялась"
            : "Не удалось отправить на печать. Проверьте принтер.";
        if (!string.IsNullOrWhiteSpace(result.Error))
            ShowToast("Ошибка печати: " + result.Error, ToastKind.Error);
        SyncPrintPageState();
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

        var activeTemplate = EnsureActiveTemplateSelection();

        _settings.NormalizePrintMode();
        AutoPrintQuickToggle.IsChecked = _settings.PrintMode == PrintMode.Auto;
        AutoPrintStatusText.Text = _settings.PrintMode == PrintMode.Auto ? "Авто" : "Ручная";
        if (PrintModeCombo != null)
        {
            var wasLoading = _isLoadingSettings;
            _isLoadingSettings = true;
            PrintModeCombo.SelectedIndex = _settings.PrintMode == PrintMode.Auto ? 1 : 0;
            _isLoadingSettings = wasLoading;
        }
        PrintTemplateText.Text = activeTemplate.Name;
        PrintPrinterText.Text = string.IsNullOrWhiteSpace(_settings.PrinterName) ? "По умолчанию" : _settings.PrinterName;
        PrintCopiesText.Text = Math.Max(1, _settings.PrintCopies).ToString();
        RenderDashboardTemplateList(activeTemplate);
        SyncPrintPageState();
        SyncTemplatesPageState();
    }

    private void RenderDashboardTemplateList(PrintTemplate activeTemplate)
    {
        DashboardTemplatesPanel.Children.Clear();
        TemplatesSummaryText.Text = $"{_printTemplates.Count} шабл. · {activeTemplate.Name}";

        foreach (var template in _printTemplates)
            DashboardTemplatesPanel.Children.Add(BuildDashboardTemplateCard(template, activeTemplate));
    }

    private Border BuildDashboardTemplateCard(PrintTemplate template, PrintTemplate activeTemplate)
    {
        var isActive = string.Equals(template.Name, activeTemplate.Name, StringComparison.OrdinalIgnoreCase);
        var card = new Border
        {
            Style = (Style)FindResource("DataPill"),
            BorderBrush = isActive ? BrushFromResource("AccentBrush") : BrushFromResource("BorderBrushSoft"),
            Background = isActive
                ? (Brush)new BrushConverter().ConvertFrom("#12243B")!
                : BrushFromResource("PanelAltBrush"),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 10),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = isActive ? "Активный шаблон" : "Сделать активным шаблоном",
            Tag = template.Name
        };
        card.MouseLeftButtonUp += (_, _) => SetActivePrintTemplate(template.Name);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });

        grid.Children.Add(new Ellipse
        {
            Width = 12,
            Height = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = isActive ? BrushFromResource("AccentBrush") : Brushes.Transparent,
            Stroke = isActive ? BrushFromResource("AccentBrush") : BrushFromResource("BorderBrushSoft"),
            StrokeThickness = 2
        });

        var text = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        text.Children.Add(new TextBlock
        {
            Text = template.Name,
            Foreground = BrushFromResource("TextBrush"),
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 12
        });
        text.Children.Add(new TextBlock
        {
            Text = $"{template.LabelWidthMm:0.#} × {template.LabelHeightMm:0.#} мм · DM {template.DataMatrixWidthMm:0.#} мм",
            Style = (Style)FindResource("MutedText"),
            FontSize = 12
        });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var preview = new Border
        {
            Background = (Brush)new BrushConverter().ConvertFrom("#E8EEF5")!,
            CornerRadius = new CornerRadius(8),
            Height = 48,
            Width = 76,
            Child = BuildMiniLabelPreview(76, 48)
        };
        Grid.SetColumn(preview, 2);
        grid.Children.Add(preview);

        card.Child = grid;
        return card;
    }

    private static Grid BuildMiniLabelPreview(double width, double height)
    {
        var root = new Grid { Width = width, Height = height, Margin = new Thickness(5) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var matrix = BuildMatrixCanvas(28, 14);
        matrix.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(matrix, 0);
        root.Children.Add(matrix);

        var text = new StackPanel { Margin = new Thickness(5, 6, 0, 0) };
        text.Children.Add(new TextBlock
        {
            Text = "GTIN",
            Foreground = (Brush)new BrushConverter().ConvertFrom("#101820")!,
            FontSize = 7,
            FontWeight = FontWeights.Bold
        });
        text.Children.Add(new TextBlock
        {
            Text = "SN",
            Foreground = (Brush)new BrushConverter().ConvertFrom("#101820")!,
            FontSize = 7,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 2, 0, 0)
        });
        Grid.SetColumn(text, 1);
        root.Children.Add(text);

        return root;
    }

    private static Canvas BuildMatrixCanvas(double size, int cells)
    {
        var canvas = new Canvas
        {
            Width = size,
            Height = size,
            Background = Brushes.White
        };

        var dark = (Brush)new BrushConverter().ConvertFrom("#101820")!;
        var cell = size / cells;
        for (var y = 0; y < cells; y++)
        {
            for (var x = 0; x < cells; x++)
            {
                var finder = x == 0 || y == 0 || (x == cells - 1 && y % 2 == 0) || (y == cells - 1 && x % 2 == 0);
                var data = ((x * 11 + y * 7 + x * y) % 5) < 2;
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

    private void OnAutoPrintQuickToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings)
            return;

        _settings.PrintMode = AutoPrintQuickToggle.IsChecked == true ? PrintMode.Auto : PrintMode.Manual;
        _settings.Save();
        LoggingService.Info("Print", "Print mode: " + _settings.PrintMode);
        RefreshPrintSettingsUi();
        SyncConnectedViews();
    }

    private void OnPrintModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings || PrintModeCombo == null || PrintModeCombo.SelectedIndex < 0)
            return;

        _settings.PrintMode = PrintModeCombo.SelectedIndex == 1 ? PrintMode.Auto : PrintMode.Manual;
        AutoPrintQuickToggle.IsChecked = _settings.PrintMode == PrintMode.Auto;
        _settings.Save();
        LoggingService.Info("Print", "Print mode: " + _settings.PrintMode);
        RefreshPrintSettingsUi();
        SyncConnectedViews();
    }

    private async void OnPrintLastClick(object sender, RoutedEventArgs e)
    {
        if (_lastSuccessfulScan == null)
        {
            LastPrintStatusText.Text = "Нет успешного ЧЗ для печати.";
            return;
        }

        if (!await EnsureSubscriptionForFeatureAsync("Печать последнего ЧЗ"))
            return;

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
        UpdatePrintStatus(result, isAutoPrint: false);
    }

    private void OnPrintSettingsClick(object sender, RoutedEventArgs e)
    {
        if (_printTemplates.Count == 0)
            _printTemplates = _printTemplateService.LoadOrCreateDefaults();

        var window = new PrintSettingsWindow(_settings, _printTemplates) { Owner = this };
        window.TemplateSelected += (_, template) => SetActivePrintTemplate(template, showToast: false);
        if (window.ShowDialog() == true && window.ResultSettings != null)
        {
            _settings = window.ResultSettings;
            EnsureActiveTemplateSelection();
            _settings.Save();
            RefreshSettingsIntoUi();
        }
    }

    private async void OnPrintTemplatesClick(object sender, RoutedEventArgs e)
    {
        if (!await EnsureSubscriptionForFeatureAsync("Управление шаблонами"))
            return;

        if (_printTemplates.Count == 0)
            _printTemplates = _printTemplateService.LoadOrCreateDefaults();

        var window = new PrintTemplatesWindow(_printTemplates, _settings.DefaultPrintTemplateName) { Owner = this };
        if (window.ShowDialog() == true)
        {
            _printTemplates = window.Templates;
            _printTemplateService.SaveTemplates(_printTemplates);
            if (!string.IsNullOrWhiteSpace(window.SelectedTemplateName)
                && _printTemplates.Any(t => string.Equals(t.Name, window.SelectedTemplateName, StringComparison.OrdinalIgnoreCase)))
            {
                _settings.DefaultPrintTemplateName = window.SelectedTemplateName;
            }
            if (!_printTemplates.Any(t => string.Equals(t.Name, _settings.DefaultPrintTemplateName, StringComparison.OrdinalIgnoreCase)))
                _settings.DefaultPrintTemplateName = _printTemplates.FirstOrDefault()?.Name
                                                     ?? PrintTemplateService.CreateDefaultTemplates()[0].Name;
            EnsureActiveTemplateSelection();
            _settings.Save();
            RefreshSettingsIntoUi();
        }
    }

    private void RefreshPreviewForActiveTemplate()
    {
        if (_lastSuccessfulScan == null)
            return;

        UpdatePreview(_lastSuccessfulScan.ParseResult, _lastSuccessfulScan.Raw, _lastSuccessfulScan.Source);
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
