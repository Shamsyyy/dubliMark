using System.IO;
using System.Windows;
using System.Windows.Controls;
using DoubleMark.Core.Export;
using DoubleMark.Core.Models;
using DoubleMark.Core.Parsing;
using DoubleMark.Core.Print;
using DoubleMark.Desktop.Services;
using DoubleMark.Desktop.Services.Cloud;
using DoubleMark.Desktop.Settings;
using DoubleMark.Desktop.Views;

namespace DoubleMark.Desktop;

public partial class MainWindow
{
    private readonly List<ScanHistoryItem> _uiHistory = new();
    private const int InMemoryHistoryLimit = CloudScanHistoryService.MaxScanHistory;
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

    private void SyncHistoryPageState()
    {
        _historyView?.SetSignedIn(_accountSnapshot.User != null || _settings.HistoryViewMode == HistoryViewMode.Local);
        SyncHistorySettingsUi();
        _historyView?.UpdateItems(_uiHistory);
        UpdateHistoryUsageUi();
    }

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
            Mode = _settings.ScannerMode switch
            {
                ScannerMode.Auto => "Авто",
                ScannerMode.Hid => "HID",
                ScannerMode.RawInput => "RawInput",
                ScannerMode.Com => "COM",
                _ => "Не выбран"
            },
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
            PrintModeLabel = _settings.PrintMode == Settings.PrintMode.Auto ? "Автоматическая" : "Ручная",
            AutoPrintEnabled = _settings.AutoPrintEnabled,
            Printers = printers,
            SelectedPrinter = string.IsNullOrWhiteSpace(_settings.PrinterName) ? "По умолчанию" : _settings.PrinterName,
            Templates = templates,
            SelectedTemplate = template.Name,
            Copies = Math.Max(1, _settings.PrintCopies),
            PrintFolder = _settings.EffectivePrintDirectory,
            LastPrintStatus = LastPrintStatusText.Text,
            QueueStatus = _settings.PrintMode == Settings.PrintMode.Auto ? "Автопечать готова" : "Ручная печать",
            TemplateSize = $"{template.LabelWidthMm:0.#} × {template.LabelHeightMm:0.#} мм",
            DataMatrixSize = $"{template.DataMatrixWidthMm:0.#} × {template.DataMatrixHeightMm:0.#} мм",
            LabelWidthMm = template.LabelWidthMm,
            LabelHeightMm = template.LabelHeightMm,
            DataMatrixWidthMm = template.DataMatrixWidthMm,
            DataMatrixHeightMm = template.DataMatrixHeightMm,
            DataMatrixXmm = template.DataMatrixXmm,
            DataMatrixYmm = template.DataMatrixYmm,
            PreviewImage = RenderActiveTemplatePreview(template)
        };
    }

    private System.Windows.Media.ImageSource? RenderActiveTemplatePreview(PrintTemplate template) =>
        TemplatePreviewRenderer.TryRender(
            template,
            _settings.LabelShowDate,
            _settings.LabelShowShipment,
            _settings.LabelShowOrder,
            _settings.LabelShipmentNumber,
            _settings.LabelOrderNumber,
            _lastSuccessfulScan?.ParseResult,
            _lastSuccessfulScan?.Raw,
            _lastSuccessfulScan?.Source ?? "Preview");

    private TemplateViewState BuildTemplateViewState()
    {
        if (_printTemplates.Count == 0)
            _printTemplates = _accountSnapshot.User == null
                ? _printTemplateService.LoadOrCreateDefaults()
                : Array.Empty<PrintTemplate>();

        var active = ResolveActiveTemplate();
        var activeName = active.Name;
        var signedIn = _accountSnapshot.User != null;
        var syncText = signedIn
            ? _userTemplateService.StatusMessage ?? "Шаблоны синхронизированы"
            : "Войдите в аккаунт для синхронизации шаблонов";

        return new TemplateViewState
        {
            ActiveTemplateName = activeName,
            IsSignedIn = signedIn,
            SyncStatusText = syncText,
            PreviewImage = RenderActiveTemplatePreview(active),
            LabelShowDate = _settings.LabelShowDate,
            LabelShowShipment = _settings.LabelShowShipment,
            LabelShowOrder = _settings.LabelShowOrder,
            LabelShipmentNumber = _settings.LabelShipmentNumber,
            LabelOrderNumber = _settings.LabelOrderNumber,
            LabelWidthMm = active.LabelWidthMm,
            LabelHeightMm = active.LabelHeightMm,
            DataMatrixWidthMm = active.DataMatrixWidthMm,
            DataMatrixHeightMm = active.DataMatrixHeightMm,
            DataMatrixXmm = active.DataMatrixXmm,
            DataMatrixYmm = active.DataMatrixYmm,
            TextBlocks = BuildTemplateTextBlockViews(active),
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
                    string.Equals(t.Name, activeName, StringComparison.OrdinalIgnoreCase),
                    string.Equals(t.Name, _settings.DefaultPrintTemplateName, StringComparison.OrdinalIgnoreCase),
                    null,
                    null))
                .ToList()
        };
    }

    private IReadOnlyList<TemplateTextBlockViewItem> BuildTemplateTextBlockViews(PrintTemplate template)
    {
        var blocks = TemplateLayoutHelper.BuildEffectiveTextBlocks(
            template,
            _settings.LabelShowDate,
            _settings.LabelShowShipment,
            _settings.LabelShowOrder);

        var timestamp = DateTimeOffset.Now;
        var source = _lastSuccessfulScan?.Source ?? "Preview";
        var code = _lastSuccessfulScan?.ParseResult.Code ?? PreviewMarkingCode;

        return blocks
            .Select(b =>
            {
                var (layout, flow) = b.GetStyle();
                return new TemplateTextBlockViewItem(
                    b.Text,
                    b.Xmm,
                    b.Ymm,
                    b.FontSizePt,
                    b.Bold,
                    MarkRenderService.SubstituteText(
                        b.Text,
                        code,
                        timestamp,
                        source,
                        _settings.LabelShipmentNumber,
                        _settings.LabelOrderNumber),
                    layout,
                    flow,
                    b.Orientation);
            })
            .ToList();
    }

    private static readonly MarkingCode PreviewMarkingCode = new()
    {
        Gtin = "04602019556479",
        Serial = "5BZqLW",
        VerificationKey = "EE11",
        VerificationCode = "DEMOCODE",
        CodeType = MarkingCodeType.Short,
        RawData = "",
        RawDataHex = ""
    };

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

        var ports = PortsCombo?.Items.OfType<string>().ToList()
                    ?? new List<string>();

        return new DiagnosticsViewState
        {
            Mode = DiagnosticModeText.Text,
            Scanner = DiagnosticScannerText.Text,
            Status = DiagnosticStatusText.Text,
            StatusKind = StatusKindFromText(DiagnosticStatusText.Text),
            LastCheck = DiagnosticLastCheckText.Text,
            GsCount = LastScanGsCountText.Text,
            Ai01 = _lastGtin,
            Ai21 = LastScanSerialText.Text,
            Ai91 = LastScanAi91Text.Text,
            Ai92 = LastScanAi92Text.Text,
            PrintMode = _settings.PrintMode.ToString(),
            Printer = string.IsNullOrWhiteSpace(_settings.PrinterName) ? "По умолчанию" : _settings.PrinterName,
            Template = _settings.DefaultPrintTemplateName ?? "—",
            LastPrintStatus = LastPrintStatusText.Text,
            AvailableComPorts = ports.Count == 0 ? "—" : string.Join(", ", ports),
            RawEscaped = _lastRawEscaped,
            RawHex = _lastRawHex,
            RawKeySummary = rawSummary,
            Warning = warning
        };
    }

    private void UpdateScanUiSnapshot(
        ParseResult r,
        string raw,
        string source,
        MarkExportResult? exportResult,
        PrintPipelineResult? printResult,
        int? imageGsCount,
        string parseError)
    {
        var code = r.Code;
        var normalized = exportResult?.NormalizedPayload
                         ?? code?.RawData
                         ?? Gs1BarcodeEncoding.NormalizeForParse(raw).Payload;
        _lastRawEscaped = FormatRawForDisplay(raw);
        _lastNormalizedEscaped = FormatRawForDisplay(normalized);
        _lastRawHex = Gs1BarcodeEncoding.ToHex(raw);
        _lastGtin = code?.Gtin ?? "—";
        _lastAi93 = code?.AdditionalField93 ?? "—";
        _lastCodeType = code == null ? "—" : CodeTypeShort(code.CodeType);
        _lastParseError = parseError;
    }

    private async Task PersistScanToHistoryAsync(
        ParseResult r,
        string raw,
        string source,
        MarkExportResult? exportResult,
        PrintPipelineResult? printResult,
        int? imageGsCount,
        string parseError)
    {
        if (!r.IsValid || r.Code == null)
        {
            LoggingService.Info("ScanHistory", "Skip: invalid parse");
            return;
        }

        if (!_settings.LocalHistoryEnabled && !_settings.CloudHistoryEnabled)
            return;

        var savedFolder = ResolveSavedFolder(exportResult, printResult) ?? "—";
        var printStatus = printResult == null
            ? "—"
            : printResult.BlockedDuplicate
                ? "Дубль заблокирован"
                : printResult.Printed
                    ? "Напечатано"
                    : string.IsNullOrWhiteSpace(printResult.Error) ? "Не печаталось" : printResult.Error;

        var built = ScanHistoryItemBuilder.FromScan(
            r,
            raw,
            source,
            exportResult,
            printResult,
            imageGsCount,
            parseError,
            _lastRawEscaped,
            _lastNormalizedEscaped,
            _lastRawHex,
            printResult?.Render?.Template.Name ?? ResolveActiveTemplate().Name,
            string.IsNullOrWhiteSpace(_settings.PrinterName) ? "По умолчанию" : _settings.PrinterName);

        var cloudFailed = false;

        if (_settings.LocalHistoryEnabled)
            _localScanHistoryService.Add(_settings, built, r);

        if (_settings.CloudHistoryEnabled && _accountSnapshot.User != null
            && CloudScanHistoryService.IsValidForCloudHistory(r))
        {
            var masked = ScanHistoryMasking.BuildMaskedPreview(raw);
            var (item, duplicateIgnored) = await _cloudScanHistoryService.AddScanAsync(
                r,
                raw,
                source,
                exportResult,
                printResult,
                imageGsCount,
                parseError,
                masked,
                _lastRawEscaped,
                _lastNormalizedEscaped,
                _lastRawHex,
                printResult?.Render?.Template.Name ?? ResolveActiveTemplate().Name,
                string.IsNullOrWhiteSpace(_settings.PrinterName) ? "По умолчанию" : _settings.PrinterName,
                printStatus,
                savedFolder);

            if (duplicateIgnored)
                return;

            if (item == null)
                cloudFailed = true;
        }

        await ReloadScanHistoryAsync();

        if (cloudFailed && _settings.CloudHistoryEnabled)
            ShowToast("Не удалось сохранить историю в Supabase. Локальная копия сохранена.", ToastKind.Warning);
    }

    private void RebuildDashboardHistoryRows()
    {
        ResultPanel.Children.Clear();
        foreach (var item in _uiHistory.Take(20))
            AddDashboardHistoryRow(item);
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
        _settings.ScannerMode = mode switch
        {
            "HID" => ScannerMode.Hid,
            "COM" => ScannerMode.Com,
            _ => ScannerMode.Auto
        };
        _settings.Save();
        var result = RestartScanner();
        if (_settings.ScannerMode == ScannerMode.Hid)
            ShowToast("HID: выберите устройство в «Настроить сканер»", ToastKind.Warning);
        else
            ShowToast(result.Message, result.Success ? ToastKind.Success : ToastKind.Warning);
        SyncConnectedViews();
    }

    private void OnPrintViewAutoPrintChanged(object? sender, bool enabled)
    {
        _settings.PrintMode = enabled ? Settings.PrintMode.Auto : Settings.PrintMode.Manual;
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

    private async void OnExportViewAutoSaveChanged(object? sender, bool enabled)
    {
        if (enabled && !await EnsureSubscriptionForFeatureAsync("Автосохранение экспорта"))
        {
            _exportView?.UpdateState(BuildExportViewState());
            return;
        }

        _settings.AutoSaveExports = enabled;
        AutoSaveCheck.IsChecked = enabled;
        _settings.Save();
        RefreshExportSettingsUi();
        SyncConnectedViews();
    }

    private async void OnHistoryCopyRequested(object? sender, ScanHistoryItem item)
    {
        if (!await EnsureSubscriptionForFeatureAsync("История сканов"))
            return;

        if (string.IsNullOrEmpty(item.RawPayload))
        {
            ShowToast("Код недоступен для копирования", ToastKind.Warning);
            return;
        }

        Clipboard.SetText(item.RawPayload);
        ShowToast("Код ЧЗ скопирован", ToastKind.Success);
    }

    private async void OnHistoryReprintRequested(object? sender, ScanHistoryItem item)
    {
        if (!await EnsureSubscriptionForFeatureAsync("Повторная печать из истории"))
            return;

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

    private async void OnHistoryDeleteRequested(object? sender, ScanHistoryItem item)
    {
        if (_settings.HistoryViewMode == HistoryViewMode.Cloud)
        {
            if (_accountSnapshot.User == null || string.IsNullOrWhiteSpace(item.CloudId))
                return;

            if (!await _cloudScanHistoryService.DeleteHistoryItemAsync(item.CloudId))
            {
                ShowToast("Не удалось удалить запись в облаке", ToastKind.Warning);
                return;
            }
        }
        else if (_settings.LocalHistoryEnabled)
        {
            if (!_localScanHistoryService.Delete(_settings, item))
            {
                ShowToast("Не удалось удалить локальную запись", ToastKind.Warning);
                return;
            }
        }

        await ReloadScanHistoryAsync();
    }

    private async void OnHistoryClearRequested(object? sender, EventArgs e)
    {
        var scope = _settings.HistoryViewMode == HistoryViewMode.Cloud ? "облачную" : "локальную";
        var confirm = MessageBox.Show(
            this,
            $"Удалить всю {scope} историю сканирования? Это действие нельзя отменить.",
            "Очистить историю",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        if (_settings.HistoryViewMode == HistoryViewMode.Cloud)
        {
            if (_accountSnapshot.User == null)
            {
                ShowToast("Войдите в аккаунт для очистки облачной истории", ToastKind.Warning);
                return;
            }

            if (!await _cloudScanHistoryService.ClearHistoryAsync())
            {
                ShowToast("Не удалось очистить историю на сервере", ToastKind.Warning);
                return;
            }
        }
        else
        {
            _localScanHistoryService.ClearStore();
        }

        await ReloadScanHistoryAsync();
        ShowToast("История очищена", ToastKind.Success);
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
