using System.IO;
using System.Windows;
using System.Windows.Controls;
using DubliMark.Core.Export;
using DubliMark.Core.Models;
using DubliMark.Core.Parsing;
using DubliMark.Core.Print;
using DubliMark.Desktop.Services;
using DubliMark.Desktop.Settings;
using DubliMark.Desktop.Views;

namespace DubliMark.Desktop;

public partial class MainWindow
{
    private readonly List<ScanHistoryItem> _uiHistory = new();
    private string _lastRawEscaped = "—";
    private string _lastNormalizedEscaped = "—";
    private string _lastRawHex = "—";
    private string _lastGtin = "—";
    private string _lastAi93 = "—";
    private string _lastCodeType = "—";
    private string _lastParseError = "";

    private void SyncConnectedViews()
    {
        SyncScannerPageState();
        SyncPrintPageState();
        SyncTemplatesPageState();
        SyncHistoryPageState();
        SyncExportPageState();
        SyncDiagnosticsPageState();
        UpdateGlobalSearchResults();
    }

    private void RefreshSettingsIntoUi()
    {
        RefreshExportSettingsUi();
        RefreshPrintSettingsUi();
        RefreshDiagnosticsUi();
        SyncConnectedViews();
    }

    private void SyncScannerPageState() =>
        _scanView?.UpdateState(BuildScanViewState());

    private void SyncPrintPageState() =>
        _printView?.UpdateState(BuildPrintViewState(), _uiHistory);

    private void SyncTemplatesPageState() =>
        _templatesView?.UpdateState(BuildTemplateViewState());

    private void SyncHistoryPageState() =>
        _historyView?.UpdateItems(_uiHistory);

    private void SyncExportPageState() =>
        _exportView?.UpdateState(BuildExportViewState());

    private void SyncDiagnosticsPageState() =>
        _diagnosticsView?.UpdateState(BuildDiagnosticsViewState());

    private ScanViewState BuildScanViewState()
    {
        var status = StatusText.Text;
        var isError = ErrorText.Text?.Length > 0 || status.Contains("ошиб", StringComparison.OrdinalIgnoreCase);
        var hasSuccess = status.Contains("подключ", StringComparison.OrdinalIgnoreCase)
                         || status.Contains("получен", StringComparison.OrdinalIgnoreCase);

        var ports = PortsCombo.Items.OfType<string>().ToList();
        var codeType = _lastCodeType == "—" ? LastScanStatusText.Text : _lastCodeType;

        return new ScanViewState
        {
            ScannerStatus = status,
            ScannerStatusKind = isError ? UiStatusKind.Error : hasSuccess ? UiStatusKind.Success : UiStatusKind.Warning,
            Mode = _settings.ScannerMode == ScannerMode.RawInput ? "HID" : "COM-порт",
            SelectedPort = PortsCombo.SelectedItem as string ?? _settings.ComPort,
            Ports = ports,
            PortHint = PortsHintText.Visibility == Visibility.Visible ? PortsHintText.Text ?? "" : "",
            WaitText = WaitText.Text,
            ValidationStatus = LastScanStatusText.Text,
            ValidationKind = StatusKindFromText(LastScanStatusText.Text),
            Gtin = _lastGtin,
            Serial = LastScanSerialText.Text,
            Ai91 = LastScanAi91Text.Text,
            Ai92 = LastScanAi92Text.Text,
            Ai93 = _lastAi93,
            GsCount = LastScanGsCountText.Text,
            Source = LastScanSourceText.Text,
            CodeType = codeType,
            RawEscaped = _lastRawEscaped,
            NormalizedEscaped = _lastNormalizedEscaped,
            RawHex = _lastRawHex,
            ErrorText = ErrorText.Text ?? "",
            PreviewImage = LastScanPreviewImage.Source
        };
    }

    private PrintViewState BuildPrintViewState()
    {
        var printers = new[] { "По умолчанию" }
            .Concat(MarkPrintService.GetInstalledPrinters())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var template = ResolveActiveTemplate();
        var templates = _printTemplates.Select(t => t.Name).ToList();

        return new PrintViewState
        {
            AutoPrintEnabled = _settings.AutoPrintEnabled,
            Printers = printers,
            SelectedPrinter = string.IsNullOrWhiteSpace(_settings.PrinterName) ? "По умолчанию" : _settings.PrinterName,
            Templates = templates,
            SelectedTemplate = template.Name,
            Copies = Math.Max(1, _settings.PrintCopies),
            PrintFolder = _settings.EffectivePrintDirectory,
            LastPrintStatus = LastPrintStatusText.Text,
            QueueStatus = _settings.AutoPrintEnabled ? "Автопечать готова" : "Ручная печать",
            TemplateSize = $"{template.LabelWidthMm:0.#} × {template.LabelHeightMm:0.#} мм",
            DataMatrixSize = $"{template.DataMatrixWidthMm:0.#} × {template.DataMatrixHeightMm:0.#} мм",
            PreviewImage = LastScanPreviewImage.Source
        };
    }

    private TemplateViewState BuildTemplateViewState()
    {
        if (_printTemplates.Count == 0)
            _printTemplates = _printTemplateService.LoadOrCreateDefaults();

        var activeName = ResolveActiveTemplate().Name;
        return new TemplateViewState
        {
            ActiveTemplateName = activeName,
            Templates = _printTemplates
                .Select(t => new TemplateViewItem(
                    t.Name,
                    t.LabelWidthMm,
                    t.LabelHeightMm,
                    t.DataMatrixWidthMm,
                    t.DataMatrixHeightMm,
                    t.DataMatrixXmm,
                    t.DataMatrixYmm,
                    t.TextBlocks.Count,
                    string.Equals(t.Name, activeName, StringComparison.OrdinalIgnoreCase)))
                .ToList()
        };
    }

    private ExportViewState BuildExportViewState()
    {
        return new ExportViewState
        {
            AutoSaveEnabled = _settings.AutoSaveExports,
            ExportFolder = _settings.EffectiveExportDirectory,
            LastSavedPath = _lastScanFolder ?? "—",
            Status = ExportStatusText.Text,
            StatusKind = ExportStatusText.Foreground == BrushFromResource("DangerBrush")
                ? UiStatusKind.Error
                : _settings.AutoSaveExports ? UiStatusKind.Success : UiStatusKind.Neutral,
            RecentFiles = RecentFiles(_settings.EffectiveExportDirectory)
        };
    }

    private DiagnosticsViewState BuildDiagnosticsViewState()
    {
        var rawDiagnostics = RawInputScannerService.LastScanDiagnostics;
        var rawSummary = rawDiagnostics == null
            ? "Нет HID-сессии"
            : string.Join(Environment.NewLine, rawDiagnostics.Keys.TakeLast(18).Select(k => k.ToLogLine()));

        var warning = string.IsNullOrWhiteSpace(_lastParseError)
            ? ""
            : _lastParseError;

        return new DiagnosticsViewState
        {
            Mode = DiagnosticModeText.Text,
            Scanner = DiagnosticScannerText.Text,
            Status = DiagnosticStatusText.Text,
            StatusKind = StatusKindFromText(DiagnosticStatusText.Text),
            LastCheck = DiagnosticLastCheckText.Text,
            GsCount = LastScanGsCountText.Text,
            Ai91 = LastScanAi91Text.Text,
            Ai92 = LastScanAi92Text.Text,
            RawKeySummary = rawSummary,
            Warning = warning
        };
    }

    private void RecordUiScanHistory(
        ParseResult r,
        string raw,
        string source,
        MarkExportResult? exportResult,
        PrintPipelineResult? printResult,
        int? imageGsCount,
        string parseError)
    {
        var code = r.Code;
        var normalized = code?.RawData ?? Gs1BarcodeEncoding.NormalizeForParse(raw).Payload;
        _lastRawEscaped = FormatRawForDisplay(raw);
        _lastNormalizedEscaped = FormatRawForDisplay(normalized);
        _lastRawHex = Gs1BarcodeEncoding.ToHex(raw);
        _lastGtin = code?.Gtin ?? "—";
        _lastAi93 = code?.AdditionalField93 ?? "—";
        _lastCodeType = code == null ? "—" : CodeTypeShort(code.CodeType);
        _lastParseError = parseError;

        var status = r.IsValid
            ? r.InfoMessages.Count > 0 ? "Предупреждение" : "Успешно"
            : "Ошибка";
        var savedFolder = ResolveSavedFolder(exportResult, printResult) ?? "—";
        var printStatus = printResult == null
            ? "—"
            : printResult.BlockedDuplicate
                ? "Дубль заблокирован"
                : printResult.Printed
                    ? "Напечатано"
                    : string.IsNullOrWhiteSpace(printResult.Error) ? "Не печаталось" : printResult.Error;

        _uiHistory.Insert(0, new ScanHistoryItem
        {
            Timestamp = DateTime.Now,
            Status = status,
            StatusKind = StatusKindFromText(status),
            Gtin = code?.Gtin ?? "—",
            Serial = code?.Serial ?? "—",
            Ai91 = code?.VerificationKey ?? "—",
            Ai92 = code?.VerificationCode ?? "—",
            Ai93 = code?.AdditionalField93 ?? "—",
            GsCount = (imageGsCount ?? Gs1BarcodeEncoding.CountGs(normalized)).ToString(),
            Source = source,
            CodeType = code == null ? "—" : CodeTypeShort(code.CodeType),
            RawEscaped = _lastRawEscaped,
            RawPayload = raw,
            NormalizedEscaped = _lastNormalizedEscaped,
            RawHex = _lastRawHex,
            Error = parseError,
            SavedFolder = savedFolder,
            Template = printResult?.Render?.Template.Name ?? (r.IsValid ? ResolveActiveTemplate().Name : "—"),
            Printer = string.IsNullOrWhiteSpace(_settings.PrinterName) ? "По умолчанию" : _settings.PrinterName,
            PrintStatus = printStatus,
            PreviewImage = LastScanPreviewImage.Source
        });

        while (_uiHistory.Count > 100)
            _uiHistory.RemoveAt(_uiHistory.Count - 1);
    }

    private void OnScanViewConnectRequested(object? sender, RoutedEventArgs e)
    {
        if (_scanView?.SelectedPort is string port)
            PortsCombo.SelectedItem = port;
        OnConnectClick(PortsCombo, e);
        SyncConnectedViews();
    }

    private void OnScanViewRefreshPortsRequested(object? sender, RoutedEventArgs e)
    {
        OnRefreshClick(sender ?? this, e);
        SyncConnectedViews();
    }

    private void OnScanViewModeSelectionRequested(object? sender, string mode)
    {
        if (mode == "HID")
        {
            ShowToast("HID подключается через «Настроить сканер»", ToastKind.Warning);
            return;
        }

        ComConnectionPanel.Visibility = Visibility.Visible;
        PortsCombo.Focusable = true;
        ShowToast("Выбран режим COM. Выберите порт и нажмите подключить.", ToastKind.Success);
    }

    private void OnPrintViewAutoPrintChanged(object? sender, bool enabled)
    {
        _settings.AutoPrintEnabled = enabled;
        AutoPrintQuickToggle.IsChecked = enabled;
        _settings.Save();
        RefreshPrintSettingsUi();
        SyncConnectedViews();
    }

    private void OnPrintViewPrinterChanged(object? sender, string? printer)
    {
        _settings.PrinterName = string.Equals(printer, "По умолчанию", StringComparison.OrdinalIgnoreCase)
            ? null
            : printer;
        _settings.Save();
        RefreshPrintSettingsUi();
        SyncConnectedViews();
    }

    private void OnPrintViewTemplateChanged(object? sender, string? template)
    {
        SetActivePrintTemplate(template, showToast: false);
    }

    private void OnTemplatesViewTemplateSelected(object? sender, string template) =>
        SetActivePrintTemplate(template);

    private void OnPrintViewCopiesChanged(object? sender, int copies)
    {
        _settings.PrintCopies = Math.Max(1, copies);
        _settings.Save();
        RefreshPrintSettingsUi();
        SyncConnectedViews();
    }

    private void OnExportViewAutoSaveChanged(object? sender, bool enabled)
    {
        _settings.AutoSaveExports = enabled;
        AutoSaveCheck.IsChecked = enabled;
        _settings.Save();
        RefreshExportSettingsUi();
        SyncConnectedViews();
    }

    private void OnHistoryOpenFolderRequested(object? sender, ScanHistoryItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.SavedFolder) && item.SavedFolder != "—")
            OpenFolder(item.SavedFolder);
    }

    private void OnHistoryCopyRequested(object? sender, ScanHistoryItem item)
    {
        Clipboard.SetText(string.Join(Environment.NewLine, new[]
        {
            $"status={item.Status}",
            $"gtin={item.Gtin}",
            $"serial={item.Serial}",
            $"ai91={item.Ai91}",
            $"ai92={item.Ai92}",
            $"ai93={item.Ai93}",
            $"gsCount={item.GsCount}",
            $"rawEscaped={item.RawEscaped}",
            $"normalizedEscaped={item.NormalizedEscaped}",
            $"rawHex={item.RawHex}"
        }));
        ShowToast("Данные скана скопированы", ToastKind.Success);
    }

    private async void OnHistoryReprintRequested(object? sender, ScanHistoryItem item)
    {
        if (string.IsNullOrEmpty(item.RawPayload) || item.StatusKind == UiStatusKind.Error)
        {
            ShowToast("Этот скан нельзя повторно печатать как готовый ЧЗ", ToastKind.Warning);
            return;
        }

        var parse = _parser.Parse(item.RawPayload);
        if (!parse.IsValid)
        {
            ShowToast("Выбранный скан больше не проходит проверку", ToastKind.Warning);
            return;
        }

        var print = await ProcessPrintAfterScanAsync(
            parse,
            item.RawPayload,
            item.Source,
            forcePrint: true,
            allowDuplicate: true);
        UpdatePrintStatus(print);
        SyncConnectedViews();
    }

    private static UiStatusKind StatusKindFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return UiStatusKind.Neutral;

        if (text.Contains("ошиб", StringComparison.OrdinalIgnoreCase)
            || text.Contains("не подключ", StringComparison.OrdinalIgnoreCase))
            return UiStatusKind.Error;
        if (text.Contains("предуп", StringComparison.OrdinalIgnoreCase)
            || text.Contains("GS", StringComparison.OrdinalIgnoreCase))
            return UiStatusKind.Warning;
        if (text.Contains("успеш", StringComparison.OrdinalIgnoreCase)
            || text.Contains("подключ", StringComparison.OrdinalIgnoreCase)
            || text.Contains("получ", StringComparison.OrdinalIgnoreCase)
            || text.Contains("напечат", StringComparison.OrdinalIgnoreCase))
            return UiStatusKind.Success;

        return UiStatusKind.Neutral;
    }

    private static string CodeTypeShort(MarkingCodeType type) =>
        type switch
        {
            MarkingCodeType.Full => "Full",
            MarkingCodeType.Short => "Short",
            _ => "Unknown"
        };

    private static IReadOnlyList<string> RecentFiles(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
                return Array.Empty<string>();

            return Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTime)
                .Take(10)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList()!;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
