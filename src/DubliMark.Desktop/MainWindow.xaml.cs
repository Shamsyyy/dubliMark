using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DubliMark.Core.Export;
using DubliMark.Core.Models;
using DubliMark.Core.Parsing;
using DubliMark.Core.Print;
using DubliMark.Desktop.Services;
using DubliMark.Desktop.Settings;
using Microsoft.Win32;

namespace DubliMark.Desktop;

public partial class MainWindow : Window
{
    private IScannerSource? _scanner;
    private AppSettings _settings = new();
    private readonly Gs1Parser _parser = new();
    private readonly MarkExportService _exportService = new();
    private string? _lastBarcode;
    private DateTime _lastBarcodeUtc = DateTime.MinValue;
    private static readonly TimeSpan BarcodeDedupeWindow = TimeSpan.FromMilliseconds(800);
    private bool _isScannerSetupInProgress;
    private bool _isLoadingSettings;
    private ScannerSetupWindow? _setupWindow;

    public MainWindow()
    {
        InitializeComponent();
        InitializePrintServices();
        InitializeNavigation();
        Loaded += OnLoaded;
        PreviewKeyDown += OnPreviewKeyDown;
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

        if (!ScannerSourceFactory.IsHidConfigured(_settings) || _scanner is not RawInputScannerService)
            return;

        if (e.Key is System.Windows.Input.Key.Return or System.Windows.Input.Key.Tab)
            e.Handled = true;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoadingSettings = true;
        _settings = AppSettings.Load();
        _printTemplates = _printTemplateService.LoadOrCreateDefaults();
        RefreshExportSettingsUi();
        RefreshPrintSettingsUi();
        _isLoadingSettings = false;
        RefreshPorts();
        SelectSavedPort();
        RestartScanner();
        SyncConnectedViews();
        Focus();
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

    private void RefreshPorts()
    {
        var ports = SerialScannerService.GetAvailablePorts();
        PortsCombo.ItemsSource = ports;

        if (ports.Length > 0)
        {
            PortsHintText.Visibility = Visibility.Collapsed;
            if (PortsCombo.SelectedIndex < 0)
                PortsCombo.SelectedIndex = 0;
        }
        else
        {
            PortsHintText.Text =
                "COM-порты не найдены. Переключите сканер в режим Virtual COM или используйте HID (кнопка «Настроить сканер»).";
            PortsHintText.Visibility = Visibility.Visible;
        }

        SyncScannerPageState();
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => RefreshPorts();

    private void OnConnectClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var port = PortsCombo.SelectedItem as string;
            if (port == null)
            {
                SetStatus("Порт не выбран", isError: true);
                return;
            }

            _settings.ComPort = port;
            _settings.ScannerMode = ScannerMode.Com;
            _settings.ScannerDevicePath = null;
            _settings.Save();

            RestartScanner();
            SetStatus($"COM {port} подключен", isError: false);
            ComConnectionPanel.Visibility = Visibility.Collapsed;
            PortsCombo.Focusable = false;
            SyncConnectedViews();
            Focus();
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            SetStatus("Ошибка: " + ex.Message, isError: true);
        }
    }

    private void OnHidDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        var dlg = new HidDiagnosticsWindow(_settings) { Owner = this };
        dlg.ShowDialog();
        Focus();
    }

    private void OnSetupScannerClick(object sender, RoutedEventArgs e)
    {
        if (_isScannerSetupInProgress || _setupWindow != null)
            return;

        _isScannerSetupInProgress = true;
        StopScanner();

        try
        {
            _setupWindow = new ScannerSetupWindow(_settings) { Owner = this };
            _setupWindow.Closed += (_, _) => _setupWindow = null;

            if (_setupWindow.ShowDialog() == true && _setupWindow.ResultSettings != null)
            {
                _settings = _setupWindow.ResultSettings;
                _settings.Save();
                ComConnectionPanel.Visibility = Visibility.Visible;
                RestartScanner();
                SetStatus("Сканер HID настроен", isError: false);
                ErrorText.Text = string.Empty;
                SyncConnectedViews();
                Focus();
            }
        }
        finally
        {
            _isScannerSetupInProgress = false;
            _setupWindow = null;
            if (_scanner == null)
                RestartScanner();
        }
    }

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

    private void RestartScanner()
    {
        StopScanner();
        _scanner = ScannerSourceFactory.Create(this, _settings);
        _scanner.BarcodeReceived += OnBarcode;

        if (_settings.ScannerMode == ScannerMode.Com && !string.IsNullOrWhiteSpace(_settings.ComPort))
        {
            ComConnectionPanel.Visibility = Visibility.Collapsed;
            PortsCombo.Focusable = false;
        }
        else
        {
            PortsCombo.Focusable = true;
        }

        UpdateStatusFromSettings();
        Focus();
    }

    private void UpdateStatusFromSettings()
    {
        switch (_settings.ScannerMode)
        {
            case ScannerMode.Com when !string.IsNullOrWhiteSpace(_settings.ComPort):
                SetStatus($"COM {_settings.ComPort} подключен", isError: false);
                break;
            case ScannerMode.RawInput when !string.IsNullOrWhiteSpace(_settings.ScannerDevicePath):
                SetStatus("HID сканер подключен", isError: false);
                break;
            default:
                SetStatus("HID не настроен — «Настроить сканер»", isError: false);
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

        var now = DateTime.UtcNow;
        if (raw == _lastBarcode && now - _lastBarcodeUtc < BarcodeDedupeWindow)
            return;

        _lastBarcode = raw;
        _lastBarcodeUtc = now;

        var result = _parser.Parse(raw);
        var source = GetScannerExportSource();
        var export = SaveExportIfEnabled(result, raw, source);
        var print = await ProcessPrintAfterScanAsync(result, raw, source, forcePrint: false, allowDuplicate: false);
        DisplayResult(result, raw, source, export, print);
    }

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
        var result = _parser.Parse(decoded.Raw);
        var export = SaveExportIfEnabled(result, decoded.Raw, source);
        var print = await ProcessPrintAfterScanAsync(result, decoded.Raw, source, forcePrint: false, allowDuplicate: false);
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
        var result = _parser.Parse(raw);
        var export = SaveExportIfEnabled(result, raw, source);
        var print = await ProcessPrintAfterScanAsync(result, raw, source, forcePrint: false, allowDuplicate: false);
        DisplayResult(result, raw, source, export, print);
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

    private MarkExportResult? SaveExportIfEnabled(ParseResult result, string raw, string source)
    {
        if (!_settings.AutoSaveExports)
            return null;

        return _exportService.Save(new MarkExportRequest
        {
            RawPayload = raw,
            ParseResult = result,
            Source = source,
            ExportRoot = _settings.EffectiveExportDirectory
        });
    }

    private string GetScannerExportSource() =>
        _settings.ScannerMode switch
        {
            ScannerMode.Com => "COM",
            ScannerMode.RawInput => "HID",
            _ => "Manual"
        };

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
        AddHistoryRow(r, printResult);
        RecordUiScanHistory(r, raw, source, exportResult, printResult, imageGsCount, parseError);
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
            ? r.InfoMessages.Count > 0 ? new SolidColorBrush(Color.FromRgb(54, 42, 18)) : new SolidColorBrush(Color.FromRgb(22, 61, 43))
            : new SolidColorBrush(Color.FromRgb(62, 23, 29));
        LastScanStatusBadgeBorder.BorderBrush = r.IsValid
            ? r.InfoMessages.Count > 0 ? BrushFromResource("WarningBrush") : BrushFromResource("SuccessBrush")
            : BrushFromResource("DangerBrush");

        var code = r.Code;
        LastScanAi91Text.Text = code?.VerificationKey ?? "—";
        LastScanAi92Text.Text = code?.VerificationCode ?? code?.AdditionalField93 ?? "—";
        LastScanSerialText.Text = code?.Serial ?? "—";
        LastScanGsCountText.Text = (imageGsCount ?? Gs1BarcodeEncoding.CountGs(code?.RawData ?? raw)).ToString();
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

        DiagnosticLastCheckText.Text = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
    }

    private void AddHistoryRow(ParseResult r, PrintPipelineResult? printResult)
    {
        var code = r.Code;
        var status = r.IsValid
            ? r.InfoMessages.Count > 0 ? "Предупреждение" : "Успешно"
            : "Ошибка";
        var statusBrush = r.IsValid
            ? r.InfoMessages.Count > 0 ? BrushFromResource("WarningBrush") : BrushFromResource("SuccessBrush")
            : BrushFromResource("DangerBrush");

        var row = new Grid
        {
            MinHeight = 30
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });

        row.Children.Add(HistoryCell(DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"), 0));
        row.Children.Add(HistoryCell(status, 1, statusBrush));
        row.Children.Add(HistoryCell(code?.VerificationKey ?? "—", 2));
        row.Children.Add(HistoryCell(code?.VerificationCode ?? code?.AdditionalField93 ?? "—", 3));
        row.Children.Add(HistoryCell(printResult?.Render?.Template.Name ?? (r.IsValid ? ResolveActiveTemplate().Name : "—"), 4));
        row.Children.Add(HistoryCell(string.IsNullOrWhiteSpace(_settings.PrinterName) ? "по умолчанию" : _settings.PrinterName, 5));

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
                Template = ResolveActiveTemplate()
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
                Background = (Brush)new BrushConverter().ConvertFrom("#3e3e42")!,
                Foreground = Brushes.White,
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
            ScannerModeCombo.SelectedIndex = _settings.ScannerMode == ScannerMode.RawInput ? 1 : 0;
            _isLoadingSettings = wasLoading;
        }

        DiagnosticModeText.Text = _settings.ScannerMode switch
        {
            ScannerMode.Com => "COM-порт",
            ScannerMode.RawInput => "HID",
            _ => "Не выбран"
        };

        DiagnosticScannerText.Text = _settings.ScannerMode switch
        {
            ScannerMode.Com when !string.IsNullOrWhiteSpace(_settings.ComPort) => _settings.ComPort,
            ScannerMode.RawInput when !string.IsNullOrWhiteSpace(_settings.ScannerDevicePath) => "Raw Input",
            _ => "—"
        };
    }

    private void OnAutoSaveChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings)
            return;

        _settings.AutoSaveExports = AutoSaveCheck.IsChecked == true;
        _settings.Save();
        RefreshExportSettingsUi();
        SyncConnectedViews();
    }

    private void OnChooseExportFolderClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Выберите папку экспорта DubliMark",
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
        _scanner.Stop();

        if (_scanner is IDisposable disposable)
            disposable.Dispose();

        _scanner = null;
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e) =>
        StopScanner();

}
