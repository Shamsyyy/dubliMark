using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
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

    private void UpdatePrintStatus(PrintPipelineResult? result)
    {
        if (result == null)
            return;

        if (result.BlockedDuplicate)
        {
            LastPrintStatusText.Text = result.Error ?? "";
            ShowToast("Повторная печать заблокирована", ToastKind.Warning);
            SyncPrintPageState();
            return;
        }

        if (result.Printed)
        {
            var folder = result.Export?.DirectoryPath;
            LastPrintStatusText.Text = string.IsNullOrWhiteSpace(folder)
                ? "Напечатано"
                : "Напечатано и сохранено: " + folder;
            ShowToast("Напечатано", ToastKind.Success);
            SyncPrintPageState();
            return;
        }

        LastPrintStatusText.Text = string.IsNullOrWhiteSpace(result.Error)
            ? "Печать не выполнялась"
            : "Ошибка печати: " + result.Error;
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

        AutoPrintQuickToggle.IsChecked = _settings.AutoPrintEnabled;
        AutoPrintStatusText.Text = _settings.AutoPrintEnabled ? "Вкл." : "Выкл.";
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
        TemplatesSummaryText.Text = $"{_printTemplates.Count} шабл. · выбран {activeTemplate.Name}";

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
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });

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
            TextTrimming = TextTrimming.CharacterEllipsis
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
            Width = 68,
            Child = new Grid
            {
                Children =
                {
                    new Rectangle
                    {
                        Fill = (Brush)new BrushConverter().ConvertFrom("#101820")!,
                        Width = Math.Clamp(template.DataMatrixWidthMm * 1.4, 20, 34),
                        Height = Math.Clamp(template.DataMatrixHeightMm * 1.4, 20, 34),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(7, 0, 0, 0)
                    },
                    new TextBlock
                    {
                        Text = "ЧЗ",
                        Foreground = (Brush)new BrushConverter().ConvertFrom("#101820")!,
                        FontWeight = FontWeights.Bold,
                        FontSize = 9,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(0, 6, 6, 0)
                    }
                }
            }
        };
        Grid.SetColumn(preview, 2);
        grid.Children.Add(preview);

        card.Child = grid;
        return card;
    }

    private void OnAutoPrintQuickToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings)
            return;

        _settings.AutoPrintEnabled = AutoPrintQuickToggle.IsChecked == true;
        _settings.Save();
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

    private void OnPrintTemplatesClick(object sender, RoutedEventArgs e)
    {
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
