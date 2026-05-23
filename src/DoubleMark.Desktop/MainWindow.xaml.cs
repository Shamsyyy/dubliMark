using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DoubleMark.Core.Export;
using DoubleMark.Core.Models;
using DoubleMark.Core.Parsing;
using DoubleMark.Core.Print;
using DoubleMark.Desktop.Services;
using DoubleMark.Desktop.Settings;
using DoubleMark.Desktop.Views;
using Microsoft.Win32;

namespace DoubleMark.Desktop;

public partial class MainWindow : Window
{
    private IScannerSource? _scanner;
    private AppSettings _settings = new();
    private readonly Gs1Parser _parser = new();
    private readonly MarkExportService _exportService = new();
    private string? _lastBarcode;
    private DateTime _lastBarcodeUtc = DateTime.MinValue;
    private static readonly TimeSpan BarcodeDedupeWindow = TimeSpan.FromMilliseconds(800);
    private volatile bool _isScannerSetupInProgress;
    private volatile bool _isLoadingSettings;
    private ScannerSetupWindow? _setupWindow;

    public MainWindow()
    {
        InitializeComponent();
        WindowWorkAreaHelper.EnableWorkAreaMaximize(this);
        InitializePrintServices();
        InitializeAccountServices();
        InitializeCloudDataServices();
        InitializeNavigation();
        Loaded += OnLoaded;
        Closed += OnClosed;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        try { _scanner?.Stop(); } catch { /* best-effort */ }
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control
            && (e.Key == System.Windows.Input.Key.K || e.Key == System.Windows.Input.Key.Oem2))
        {
            FocusGlobalSearch();
            e.Handled = true;
            return;
        }

        if (_isScannerSetupInProgress || _setupWindow != null)
            return;

        if (_scanner == null)
            return;

        if (_settings.ScannerMode is not (ScannerMode.Hid or ScannerMode.Auto or ScannerMode.RawInput))
            return;

        if (e.Key is System.Windows.Input.Key.Return or System.Windows.Input.Key.Tab)
            e.Handled = true;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => _ = InitializeOnLoadedAsync();

    private async Task InitializeOnLoadedAsync()
    {
        try
        {
            _isLoadingSettings = true;
            AppDataMigrationService.MigrateLegacyData();
            SidebarVersionText.Text = AppReleaseInfoProvider.Current.VersionLabel;
            ScannerSourceFactory.ResetHidBindingSession();
            _settings = AppSettings.Load();
            _settings.ApplyStartupScannerMode();
            _settings.Save();
            _printTemplates = _printTemplateService.LoadOrCreateDefaults();
            RefreshExportSettingsUi();
            RefreshPrintSettingsUi();
            _isLoadingSettings = false;
            InitializeScannerUi();
            RefreshPorts();
            SelectSavedPort();
            RestartScanner();
            SyncConnectedViews();
            await ReloadScanHistoryAsync();
            await RestoreAccountOnStartupAsync();
            _ = CheckForUpdatesOnStartupAsync();
            Focus();
        }
        catch (Exception ex)
        {
            LoggingService.Error("Startup", "Init failed", ex);
        }
    }

    private void SelectSavedPort()
    {
        if (string.IsNullOrWhiteSpace(_settings.ComPort))
            return;

        for (int i = 0; i < PortsCombo.Items.Count; i++)
        {
            if (string.Equals(PortsCombo.Items[i]?.ToString(), _settings.ComPort, StringComparison.OrdinalIgnoreCase))
            {
                PortsCombo.SelectedIndex = i;
                return;
            }
        }
    }

    private void OnHidDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        var dlg = new HidDiagnosticsWindow(_settings) { Owner = this };
        dlg.ShowDialog();
        Focus();
    }

    private void OnSetupScannerClick(object sender, RoutedEventArgs e) =>
        OnSetupScannerClickSafe(sender, e);

    private void OnResetSettingsClick(object sender, RoutedEventArgs e)
    {
        _settings.Reset();
        _settings = AppSettings.Load();
        RefreshExportSettingsUi();
        RefreshPrintSettingsUi();
        ComConnectionPanel.Visibility = Visibility.Visible;
        PortsCombo.Focusable = true;
        RefreshPorts();
        RestartScanner();
        SetStatus("Настройки сброшены", isError: false);
        ErrorText.Text = string.Empty;
        WaitText.Text = "Ожидание сканирования...";
        SyncConnectedViews();
    }

    private void UpdateStatusFromSettings()
    {
        switch (_settings.ScannerMode)
        {
            case ScannerMode.Auto when _scanner is IScannerTransportAware { ActiveTransportSummary: { Length: > 0 } s }:
                SetStatus($"Авто: {s}", isError: false);
                break;
            case ScannerMode.Auto:
                SetStatus("Авто: подключение сканера…", isError: false);
                break;
            case ScannerMode.Com when !string.IsNullOrWhiteSpace(_settings.ComPort):
                SetStatus($"COM {_settings.ComPort} подключен", isError: false);
                break;
            case ScannerMode.Hid when ScannerSourceFactory.IsHidConfigured(_settings):
                SetStatus("HID сканер подключен", isError: false);
                break;
            case ScannerMode.Hid:
                SetStatus("HID не настроен — «Настройки»", isError: true);
                break;
            case ScannerMode.RawInput:
                SetStatus(ScannerSourceFactory.IsRawInputConfigured(_settings)
                    ? "RawInput включён"
                    : "RawInput не настроен — «Настройки»",
                    isError: false);
                break;
            default:
                SetStatus("Сканер не настроен — «Настройки»", isError: false);
                break;
        }

        RefreshDiagnosticsUi();
    }

    private void SetStatus(string text, bool isError)
    {
        StatusText.Text = text;
        StatusText.Foreground = isError ? Brushes.OrangeRed : Brushes.LightGreen;
        ScannerStatusDot.Fill = isError ? BrushFromResource("DangerBrush") : BrushFromResource("SuccessBrush");
        WorkspaceStatusDot.Fill = isError ? BrushFromResource("WarningBrush") : BrushFromResource("SuccessBrush");
        DiagnosticStatusText.Text = text;
        DiagnosticStatusText.Foreground = isError ? BrushFromResource("DangerBrush") : BrushFromResource("SuccessBrush");
        DiagnosticLastCheckText.Text = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");

        if (IsLoaded)
            ShowToast(text, isError ? ToastKind.Error : ToastKind.Success);
        SyncConnectedViews();
    }

    private void OnBarcode(object? sender, string raw)
    {
        if (_isScannerSetupInProgress || _setupWindow != null)
            return;

        _ = Dispatcher.InvokeAsync(() => HandleBarcodeOnUiAsync(raw));
    }

    private async Task HandleBarcodeOnUiAsync(string raw)
    {
        if (_isScannerSetupInProgress || _setupWindow != null)
            return;

        var source = GetScannerExportSource();
        var rebound = TryAutoBindHidDevice(source);
        await OnScanCompletedAsync(raw, source);

        if (rebound && _settings.ScannerMode is ScannerMode.Hid or ScannerMode.Auto)
            RestartScanner();
    }

    private bool TryAutoBindHidDevice(string source)
    {
        if (_settings.ScannerMode is not (ScannerMode.Hid or ScannerMode.Auto))
            return false;

        if (source is not ("HID" or "Auto"))
            return false;

        if (!HidDeviceAutoBinder.TryBindFromLastScan(_settings, out var message))
            return false;

        if (!string.IsNullOrWhiteSpace(message))
            ShowToast(message, ToastKind.Success);
        return true;
    }

    private async Task<(ParseResult Result, MarkExportResult? Export, PrintPipelineResult? Print)> ProcessScanCoreAsync(
        string raw,
        string source)
    {
        if (string.IsNullOrEmpty(raw))
        {
            LoggingService.Warn("Scanner", "Empty scan ignored");
            return (new ParseResult { IsValid = false, ErrorMessage = "Пустой скан" }, null, null);
        }

        var now = DateTime.UtcNow;
        if (raw == _lastBarcode && now - _lastBarcodeUtc < BarcodeDedupeWindow)
        {
            LoggingService.Debug("Scanner", "Duplicate scan ignored (barcode debounce)");
            return (new ParseResult { IsValid = false, ErrorMessage = "Повторный скан проигнорирован" }, null, null);
        }

        _lastBarcode = raw;
        _lastBarcodeUtc = now;

        ScanDiagnosticsHelper.LogScanReceived(source, raw);

        var result = AddScanContextWarnings(
            MarkingCodeIntegrity.Enrich(_parser.Parse(raw), raw),
            raw,
            source);
        ScanDiagnosticsHelper.LogParseResult(source, result, raw);
        UpdateScanDiagnostics(source, raw, result);

        var export = await SaveExportIfEnabledAsync(result, raw, source);
        var print = await ProcessPrintAfterScanAsync(result, raw, source, forcePrint: false, allowDuplicate: false);
        return (result, export, print);
    }

    private async Task OnScanCompletedAsync(string raw, string source)
    {
        var (result, export, print) = await ProcessScanCoreAsync(raw, source);
        if (result.ErrorMessage == "Повторный скан проигнорирован")
        {
            ShowToast("Повторный скан проигнорирован", ToastKind.Warning);
            return;
        }

        DisplayResult(result, raw, source, export, print);
    }

    private ParseResult AddScanContextWarnings(ParseResult result, string raw, string source)
    {
        if (!result.IsValid || result.Code == null)
            return result;
        if (!string.Equals(source, "HID", StringComparison.OrdinalIgnoreCase))
            return result;
        if (result.Code.CodeType != MarkingCodeType.Short || raw.Contains(Gs1BarcodeEncoding.GsChar))
            return result;

        var conflict = _uiHistory.FirstOrDefault(item =>
            string.Equals(item.Source, "HID", StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.Gtin, result.Code.Gtin, StringComparison.Ordinal)
            && string.Equals(item.Ai93, result.Code.AdditionalField93 ?? "—", StringComparison.Ordinal)
            && IsLikelySameCodeWithConflictingSerial(result.Code.Serial, item.Serial));

        if (conflict == null)
            return result;

        var messages = result.InfoMessages
            .Append("Повторный скан того же GTIN/AI93 отличается в серийном номере как возможное клавиатурное искажение HID. " +
                    "Пересканируйте код в окне DoubleMark или используйте Virtual COM.")
            .ToArray();
        return result with { InfoMessages = messages };
    }

    // Moved to DoubleMark.Core.Parsing.HidConflictDetector
    private static bool IsLikelySameCodeWithConflictingSerial(string current, string previous) =>
        HidConflictDetector.IsLikelySameCodeWithConflictingSerial(current, previous);

    private void OnLoadImageClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Выберите изображение с DataMatrix",
            Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.webp|Все файлы|*.*"
        };

        if (dlg.ShowDialog() != true)
            return;

        DecodeAndDisplayFromImage(dlg.FileName);
    }

    private async void OnPasteImageClick(object sender, RoutedEventArgs e)
    {
        if (!Clipboard.ContainsImage())
        {
            ErrorText.Text = "В буфере обмена нет изображения. Скопируйте фото или снимок экрана и повторите.";
            return;
        }

        if (Clipboard.GetImage() is not BitmapSource bitmap)
        {
            ErrorText.Text = "Не удалось прочитать изображение из буфера обмена.";
            return;
        }

        if (!ImageBarcodeDecoder.TryDecodeFromBitmap(bitmap, out var decoded, out var error))
        {
            ErrorText.Text = FriendlyDecodeError(error);
            return;
        }

        await ProcessDecodedImageAsync(decoded!, "Image");
    }

    private async void DecodeAndDisplayFromImage(string path)
    {
        if (!ImageBarcodeDecoder.TryDecodeFromFile(path, out var decoded, out var error))
        {
            ErrorText.Text = FriendlyDecodeError(error);
            return;
        }

        await ProcessDecodedImageAsync(decoded!, "Image");
    }

    private async Task ProcessDecodedImageAsync(ImageDecodeResult decoded, string source)
    {
        ErrorText.Text = decoded.NormalizeNote ?? string.Empty;
        var (result, export, print) = await ProcessScanCoreAsync(decoded.Raw, source);
        DisplayResult(
            result,
            decoded.Raw,
            source,
            export,
            print,
            imageHex: decoded.RawHex,
            imageGsCount: decoded.GsCount,
            imagePayloadByteLength: decoded.PayloadByteLength,
            imageNormalizeNote: decoded.NormalizeNote);
    }

    private async Task ProcessDecodedRaw(string raw, string source)
    {
        ErrorText.Text = string.Empty;
        await OnScanCompletedAsync(raw, source);
    }

    private static string FriendlyDecodeError(string? error) =>
        error switch
        {
            null or "" => "Не удалось распознать код на изображении.",
            "Код на изображении не найден." =>
                "Код на изображении не найден. Сфотографируйте DataMatrix целиком, без бликов и размытия.",
            "Пустой результат декодирования." => "Код распознан, но данные пусты. Попробуйте другое фото.",
            var msg when msg.Contains("Декодер не смог прочитать GS1", StringComparison.Ordinal) => msg,
            _ => $"Ошибка чтения изображения: {error}"
        };

    private async Task<MarkExportResult?> SaveExportIfEnabledAsync(ParseResult result, string raw, string source)
    {
        if (!_settings.AutoSaveExports)
            return null;

        if (!await EnsureSubscriptionForFeatureAsync("Экспорт"))
        {
            ExportStatusText.Text = "Экспорт заблокирован: нужна активная подписка DoubleMark.";
            ExportStatusText.Foreground = BrushFromResource("WarningBrush");
            return null;
        }

        return _exportService.Save(new MarkExportRequest
        {
            RawPayload = raw,
            ParseResult = result,
            Source = source,
            ExportRoot = _settings.EffectiveExportDirectory
        });
    }

    private string GetScannerExportSource()
    {
        if (_scanner is IScannerTransportAware aware
            && !string.IsNullOrWhiteSpace(aware.LastBarcodeTransport))
        {
            return aware.LastBarcodeTransport;
        }

        return _settings.ScannerMode switch
        {
            ScannerMode.Auto => "Auto",
            ScannerMode.Com => "COM",
            ScannerMode.Hid => "HID",
            ScannerMode.RawInput => "RawInput",
            _ => "Manual"
        };
    }

    private void DisplayResult(
        ParseResult r,
        string raw,
        string source,
        MarkExportResult? exportResult = null,
        PrintPipelineResult? printResult = null,
        string? imageHex = null,
        int? imageGsCount = null,
        int? imagePayloadByteLength = null,
        string? imageNormalizeNote = null)
    {
        var fromImage = imageHex != null;
        var parseError = r.IsValid
            ? string.Empty
            : FormatParseError(r, fromImage, imageGsCount ?? 0, imagePayloadByteLength);

        ErrorText.Text = parseError;
        WaitText.Text = r.IsValid ? "Код получен" : "Ошибка сканирования";

        UpdateLastScanDashboard(r, raw, source, exportResult, printResult, imageGsCount, parseError);
        UpdateScanUiSnapshot(r, raw, source, exportResult, printResult, imageGsCount, parseError);
        _ = PersistScanToHistoryAsync(r, raw, source, exportResult, printResult, imageGsCount, parseError);
        ShowScanToast(r, exportResult, printResult);
        SyncConnectedViews();

        while (ResultPanel.Children.Count > 20)
            ResultPanel.Children.RemoveAt(ResultPanel.Children.Count - 1);

        if (r.IsValid && exportResult is { Success: false, Error.Length: > 0 })
        {
            ErrorText.Text = string.IsNullOrEmpty(ErrorText.Text)
                ? "Ошибка сохранения: " + exportResult.Error
                : ErrorText.Text + Environment.NewLine + "Ошибка сохранения: " + exportResult.Error;
        }

        Focus();
    }

    private void UpdateLastScanDashboard(
        ParseResult r,
        string raw,
        string source,
        MarkExportResult? exportResult,
        PrintPipelineResult? printResult,
        int? imageGsCount,
        string parseError)
    {
        var status = r.IsValid
            ? r.InfoMessages.Count > 0 ? "Предупреждение" : "Успешно"
            : "Ошибка";
        LastScanStatusText.Text = status;
        LastScanStatusText.Foreground = r.IsValid
            ? r.InfoMessages.Count > 0 ? BrushFromResource("WarningBrush") : BrushFromResource("SuccessBrush")
            : BrushFromResource("DangerBrush");
        LastScanStatusBadgeBorder.Background = r.IsValid
            ? r.InfoMessages.Count > 0 ? BrushFromResource("WarningBadgeBackgroundBrush") : BrushFromResource("SuccessBadgeBackgroundBrush")
            : BrushFromResource("DangerBadgeBackgroundBrush");
        LastScanStatusBadgeBorder.BorderBrush = r.IsValid
            ? r.InfoMessages.Count > 0 ? BrushFromResource("WarningBrush") : BrushFromResource("SuccessBrush")
            : BrushFromResource("DangerBrush");

        var code = r.Code;
        LastScanAi91Text.Text = code?.VerificationKey ?? "—";
        LastScanAi92Text.Text = code?.VerificationCode ?? code?.AdditionalField93 ?? "—";
        LastScanSerialText.Text = code?.Serial ?? "—";
        var displayedPayload = exportResult?.NormalizedPayload ?? code?.RawData ?? raw;
        LastScanGsCountText.Text = (imageGsCount ?? Gs1BarcodeEncoding.CountGs(displayedPayload)).ToString();
        LastScanSourceText.Text = source;

        var savedPath = ResolveSavedFolder(exportResult, printResult);
        _lastScanFolder = savedPath;
        LastScanSavePathText.Text = savedPath ?? (r.IsValid ? "не сохранено" : "не сохраняется как готовый ЧЗ");
        OpenLastScanFolderButton.IsEnabled = !string.IsNullOrWhiteSpace(savedPath);

        ExportStatusText.Text = BuildExportStatus(exportResult, r.IsValid);
        ExportStatusText.Foreground = exportResult is { Success: false }
            ? BrushFromResource("DangerBrush")
            : BrushFromResource("SuccessBrush");

        _lastScanCopyText = BuildLastScanCopyText(r, raw, source, exportResult, printResult, parseError);
        UpdatePreview(r, raw, source);

        LastScanTimeText.Text = "Время скана: " + ScanHistoryFormats.FormatTimestamp(DateTime.Now);
        DiagnosticLastCheckText.Text = ScanHistoryFormats.FormatTimestamp(DateTime.Now);
    }

    private void AddDashboardHistoryRow(ScanHistoryItem item)
    {
        var statusBrush = item.StatusKind switch
        {
            UiStatusKind.Success => BrushFromResource("SuccessBrush"),
            UiStatusKind.Warning => BrushFromResource("WarningBrush"),
            UiStatusKind.Error => BrushFromResource("DangerBrush"),
            _ => BrushFromResource("MutedTextBrush")
        };

        var row = new Grid { MinHeight = 30 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(168) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });

        row.Children.Add(HistoryCell(ScanHistoryFormats.FormatTimestamp(item.Timestamp), 0));
        row.Children.Add(HistoryCell(item.Status, 1, statusBrush));
        row.Children.Add(HistoryCell(item.Ai91, 2));
        row.Children.Add(HistoryCell(item.Ai92 != "—" ? item.Ai92 : item.Ai93, 3));
        row.Children.Add(HistoryCell(item.Template, 4));
        row.Children.Add(HistoryCell(item.Printer, 5));

        var shell = new Border
        {
            Background = (Brush)FindResource("SoftPanelBrush"),
            BorderBrush = BrushFromResource("BorderBrushSoft"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 4, 10, 4),
            Margin = new Thickness(0, 0, 0, 6),
            Child = row
        };

        ResultPanel.Children.Insert(0, shell);
    }

    private TextBlock HistoryCell(string text, int column, Brush? foreground = null)
    {
        var tb = new TextBlock
        {
            Text = text,
            Foreground = foreground ?? BrushFromResource("TextBrush"),
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 12, 4),
            Opacity = foreground == null ? 0.86 : 1
        };
        Grid.SetColumn(tb, column);
        return tb;
    }

    private void UpdatePreview(ParseResult r, string raw, string source)
    {
        LastScanPreviewImage.Source = null;
        LastScanPreviewPlaceholder.Visibility = Visibility.Visible;

        if (!r.IsValid || r.Code == null)
            return;

        try
        {
            var render = new MarkRenderService().Render(new MarkRenderRequest
            {
                RawPayload = raw,
                ParseResult = r,
                Source = source,
                Template = ResolveActiveTemplate(),
                ShowDate = _settings.LabelShowDate,
                ShowShipment = _settings.LabelShowShipment,
                ShowOrder = _settings.LabelShowOrder,
                ShipmentNumber = _settings.LabelShipmentNumber,
                OrderNumber = _settings.LabelOrderNumber
            });

            using var ms = new MemoryStream(render.PngBytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();

            LastScanPreviewImage.Source = bitmap;
            LastScanPreviewPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch
        {
            LastScanPreviewImage.Source = null;
            LastScanPreviewPlaceholder.Visibility = Visibility.Visible;
        }
    }

    private static string? ResolveSavedFolder(MarkExportResult? exportResult, PrintPipelineResult? printResult)
    {
        if (exportResult is { Success: true, ExportDirectory.Length: > 0 })
            return exportResult.ExportDirectory;
        if (printResult?.Export?.DirectoryPath is { Length: > 0 } printFolder)
            return printFolder;
        if (exportResult?.DiagnosticsFilePath is { Length: > 0 } diag)
            return Path.GetDirectoryName(diag);
        return null;
    }

    private static string BuildExportStatus(MarkExportResult? exportResult, bool validCode)
    {
        if (exportResult == null)
            return "Автосохранение выключено.";
        if (validCode && exportResult.Success)
            return "Сохранено локально: " + exportResult.ExportDirectory;
        if (!validCode && !string.IsNullOrWhiteSpace(exportResult.DiagnosticsFilePath))
            return "Диагностика сохранена: " + exportResult.DiagnosticsFilePath;
        if (!string.IsNullOrWhiteSpace(exportResult.Error))
            return "Ошибка сохранения: " + exportResult.Error;
        return "Файлы сохраняются локально по дате.";
    }

    private static string BuildLastScanCopyText(
        ParseResult r,
        string raw,
        string source,
        MarkExportResult? exportResult,
        PrintPipelineResult? printResult,
        string parseError)
    {
        var code = r.Code;
        return string.Join(Environment.NewLine, new[]
        {
            "status=" + (r.IsValid ? "valid" : "invalid"),
            "error=" + parseError,
            "source=" + source,
            "gtin=" + (code?.Gtin ?? ""),
            "serial=" + (code?.Serial ?? ""),
            "ai91=" + (code?.VerificationKey ?? ""),
            "ai92=" + (code?.VerificationCode ?? ""),
            "ai93=" + (code?.AdditionalField93 ?? ""),
            "gsCount=" + Gs1BarcodeEncoding.CountGs(code?.RawData ?? raw),
            "rawEscaped=" + FormatRawForDisplay(raw),
            "export=" + (exportResult?.ExportDirectory ?? ""),
            "print=" + (printResult?.Export?.DirectoryPath ?? "")
        });
    }

    private Brush BrushFromResource(string key) =>
        (Brush)FindResource(key);

    private static string FormatParseError(
        ParseResult r,
        bool fromImage,
        int gsCount,
        int? payloadByteLength = null)
    {
        if (fromImage && r.ErrorCode == ParseErrorCode.NoGtin)
        {
            return "Код не начинается с AI 01 после нормализации. ZXing мог неверно прочитать матрицу — снимите код крупнее, без бликов.";
        }

        if (fromImage && r.ErrorCode == ParseErrorCode.NoGsSeparator && gsCount == 0)
        {
            return "В декодированных байтах нет 0x1D. Попробуйте чёткое фото DataMatrix целиком.";
        }

        if (fromImage && r.ErrorCode == ParseErrorCode.TruncatedPayload)
            return r.ErrorMessage ?? "Неполный полный код: обрезан блок 91/92.";

        return r.ErrorMessage ?? "Ошибка разбора";
    }

    private static string FormatSerialDisplay(string serial)
    {
        var len = serial.Length;
        return len == Gs1Parser.ExpectedSerialLength
            ? serial
            : $"{serial} ({len} симв., ожидается {Gs1Parser.ExpectedSerialLength})";
    }

    private static string FormatCodeType(MarkingCode code, bool fromImage)
    {
        return code.CodeType switch
        {
            MarkingCodeType.Full =>
                "Полный код маркировки (~80+ байт, AI 91/92)",
            MarkingCodeType.Short when fromImage =>
                "Короткий код маркировки (DataMatrix ~22×24). Код прочитан полностью; " +
                "криптохвост 91/92 в этой матрице не помещается.",
            MarkingCodeType.Short =>
                "Короткий код маркировки (~30 байт, без AI 91/92)",
            _ => "Неизвестный тип"
        };
    }

    private static string FormatRawForDisplay(string raw) =>
        raw.Replace("\u001D", "[GS]");

    private void AddExportResult(StackPanel sp, MarkExportResult? exportResult, bool validCode)
    {
        if (exportResult == null)
        {
            sp.Children.Add(Field("Автосохранение", "выключено", small: true));
            return;
        }

        if (validCode && exportResult.Success && exportResult.Files != null)
        {
            sp.Children.Add(Field("Сохранено", exportResult.ExportDirectory ?? "", small: true));
            var button = new Button
            {
                Content = "Открыть папку",
                Tag = exportResult.ExportDirectory,
                Background = BrushFromResource("PanelAltBrush"),
                Foreground = BrushFromResource("TextBrush"),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 6, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            button.Click += OnOpenExportFolderClick;
            sp.Children.Add(button);
            return;
        }

        if (!validCode && !string.IsNullOrWhiteSpace(exportResult.DiagnosticsFilePath))
            sp.Children.Add(Field("Диагностика", exportResult.DiagnosticsFilePath, small: true));
        else if (validCode && !string.IsNullOrWhiteSpace(exportResult.Error))
            sp.Children.Add(Field("Ошибка сохранения", exportResult.Error, small: true));
    }

    private static TextBlock Field(string label, string value, bool small = false) =>
        new()
        {
            Text = $"{label}: {value}",
            Foreground = Brushes.White,
            FontSize = small ? 11 : 14,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 2)
        };

    private void RefreshExportSettingsUi()
    {
        AutoSaveCheck.IsChecked = _settings.AutoSaveExports;
        ExportPathText.Text = _settings.EffectiveExportDirectory;
        ExportStatusText.Text = _settings.AutoSaveExports
            ? "Файлы сохраняются локально по дате."
            : "Автосохранение выключено.";
        ExportStatusText.Foreground = _settings.AutoSaveExports
            ? BrushFromResource("SuccessBrush")
            : BrushFromResource("MutedTextBrush");
        SyncExportPageState();
    }

    private void RefreshDiagnosticsUi()
    {
        if (ScannerModeCombo != null)
        {
            var wasLoading = _isLoadingSettings;
            _isLoadingSettings = true;
            ScannerModeCombo.SelectedIndex = _settings.ScannerMode switch
            {
                ScannerMode.Com => 1,
                ScannerMode.Hid => 2,
                ScannerMode.RawInput => 2,
                _ => 0
            };
            _isLoadingSettings = wasLoading;
        }

        DiagnosticModeText.Text = _settings.ScannerMode switch
        {
            ScannerMode.Auto => "Авто (COM + HID)",
            ScannerMode.Com => "COM",
            ScannerMode.Hid => "HID",
            ScannerMode.RawInput => "RawInput",
            _ => "Не выбран"
        };

        DiagnosticScannerText.Text = _settings.ScannerMode switch
        {
            ScannerMode.Com when !string.IsNullOrWhiteSpace(_settings.ComPort) => _settings.ComPort,
            ScannerMode.Hid when !string.IsNullOrWhiteSpace(_settings.EffectiveHidDevicePath) =>
                _settings.EffectiveHidDevicePath!,
            ScannerMode.RawInput => string.IsNullOrWhiteSpace(_settings.SelectedRawInputDeviceId)
                ? "Все клавиатуры"
                : _settings.SelectedRawInputDeviceId!,
            _ => "—"
        };
    }

    private async void OnAutoSaveChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings)
            return;

        if (AutoSaveCheck.IsChecked == true && !await EnsureSubscriptionForFeatureAsync("Автосохранение экспорта"))
        {
            AutoSaveCheck.IsChecked = false;
            return;
        }

        _settings.AutoSaveExports = AutoSaveCheck.IsChecked == true;
        _settings.Save();
        RefreshExportSettingsUi();
        SyncConnectedViews();
    }

    private async void OnChooseExportFolderClick(object sender, RoutedEventArgs e)
    {
        if (!await EnsureSubscriptionForFeatureAsync("Настройка экспорта"))
            return;

        var dlg = new OpenFolderDialog
        {
            Title = "Выберите папку экспорта DoubleMark",
            InitialDirectory = Directory.Exists(_settings.EffectiveExportDirectory)
                ? _settings.EffectiveExportDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        };

        if (dlg.ShowDialog() != true)
            return;

        _settings.ExportDirectory = dlg.FolderName;
        _settings.Save();
        RefreshExportSettingsUi();
        SyncConnectedViews();
    }

    private void OnOpenExportFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string folder })
            OpenFolder(folder);
    }

    private void StopScanner()
    {
        if (_scanner == null)
            return;

        _scanner.BarcodeReceived -= OnBarcode;
        if (_scanner is SerialScannerService serial)
            serial.ConnectionLost -= OnComConnectionLost;
        else if (_scanner is AutoScannerSource auto)
            auto.ConnectionLost -= OnComConnectionLost;

        _scanner.Stop();

        if (_scanner is IDisposable disposable)
            disposable.Dispose();

        _scanner = null;
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e) =>
        StopScanner();
}
