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
        Loaded += OnLoaded;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
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
            SetStatus($"✓ COM {port}", isError: false);
            ComConnectionPanel.Visibility = Visibility.Collapsed;
            PortsCombo.Focusable = false;
            Focus();
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            SetStatus("✗ " + ex.Message, isError: true);
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
                SetStatus("✓ Сканер HID настроен", isError: false);
                ErrorText.Text = string.Empty;
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
        WaitText.Text = "⏳ Ожидание сканирования...";
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
            PortsCombo.Focusable = false;
        }

        UpdateStatusFromSettings();
        Focus();
    }

    private void UpdateStatusFromSettings()
    {
        switch (_settings.ScannerMode)
        {
            case ScannerMode.Com when !string.IsNullOrWhiteSpace(_settings.ComPort):
                SetStatus($"✓ COM {_settings.ComPort}", isError: false);
                break;
            case ScannerMode.RawInput when !string.IsNullOrWhiteSpace(_settings.ScannerDevicePath):
                SetStatus("✓ HID сканер", isError: false);
                break;
            default:
                SetStatus("HID не настроен — «Настроить сканер»", isError: false);
                break;
        }
    }

    private void SetStatus(string text, bool isError)
    {
        StatusText.Text = text;
        StatusText.Foreground = isError ? Brushes.OrangeRed : Brushes.LightGreen;
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
        WaitText.Text = "✅ Код получен";

        var bg = r.IsValid ? "#1a3a1a" : "#3a1a1a";
        var card = new Border
        {
            Background = (Brush)new BrushConverter().ConvertFrom(bg)!,
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8),
            CornerRadius = new CornerRadius(4)
        };
        var sp = new StackPanel();

        sp.Children.Add(Field("Источник", source));
        sp.Children.Add(Field("Сырые данные", FormatRawForDisplay(raw), small: true));

        if (fromImage)
        {
            sp.Children.Add(Field("GS (0x1D)", (imageGsCount ?? 0).ToString()));
            if (imagePayloadByteLength is > 0)
            {
                var lengthHint = imagePayloadByteLength switch
                {
                    >= 70 => "полный код с криптохвостом 91/92",
                    >= 25 and <= 45 => "короткий код (DataMatrix ~22×22–24×24)",
                    _ => "проверьте тип кода"
                };
                sp.Children.Add(Field(
                    "Длина payload",
                    $"{imagePayloadByteLength} байт — {lengthHint}",
                    small: true));
            }

            if (!string.IsNullOrEmpty(imageNormalizeNote))
                sp.Children.Add(Field("Нормализация", imageNormalizeNote, small: true));
            sp.Children.Add(Field("HEX (изображение)", imageHex!, small: true));
        }

        if (r.IsValid && r.Code != null)
        {
            sp.Children.Add(Field("Тип кода", FormatCodeType(r.Code, fromImage)));
            sp.Children.Add(Field("GTIN", r.Code.Gtin));
            sp.Children.Add(Field("Серийный (13)", FormatSerialDisplay(r.Code.Serial)));
            if (r.Code.VerificationKey != null)
                sp.Children.Add(Field("Ключ проверки (AI 91)", r.Code.VerificationKey));
            if (r.Code.VerificationCode != null)
                sp.Children.Add(Field("Код проверки (AI 92)", r.Code.VerificationCode));
            if (r.Code.AdditionalField93 != null)
            {
                var label = r.Code.CodeType == MarkingCodeType.Short
                    ? "Код проверки (AI 93)"
                    : "Доп. поле (AI 93)";
                sp.Children.Add(Field(label, r.Code.AdditionalField93));
            }

            foreach (var info in r.InfoMessages)
            {
                sp.Children.Add(new TextBlock
                {
                    Text = "ℹ " + info,
                    Foreground = Brushes.LightGoldenrodYellow,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 2)
                });
            }

            if (!fromImage)
                sp.Children.Add(Field("HEX", r.Code.RawDataHex, small: true));

            AddExportResult(sp, exportResult, r.IsValid);
            AddPrintResult(sp, printResult);
        }
        else
        {
            sp.Children.Add(new TextBlock
            {
                Text = $"❌ {parseError}",
                Foreground = Brushes.OrangeRed,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap
            });
            if (!fromImage && r.Code != null)
                sp.Children.Add(Field("HEX", r.Code.RawDataHex, small: true));

            AddExportResult(sp, exportResult, r.IsValid);
            AddPrintResult(sp, printResult);
        }

        card.Child = sp;
        ResultPanel.Children.Insert(0, card);

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
    }

    private void OnAutoSaveChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings)
            return;

        _settings.AutoSaveExports = AutoSaveCheck.IsChecked == true;
        _settings.Save();
        RefreshExportSettingsUi();
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
