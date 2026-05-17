using System.Text;
using System.Windows;
using DoubleMark.Core.Models;
using DoubleMark.Core.Parsing;
using DoubleMark.Desktop.Services;
using DoubleMark.Desktop.Settings;

namespace DoubleMark.Desktop;

public partial class HidDiagnosticsWindow : Window
{
    private RawInputScannerService? _scanner;
    private readonly Gs1Parser _parser = new();
    private readonly StringBuilder _liveLog = new();

    public HidDiagnosticsWindow(AppSettings settings)
    {
        InitializeComponent();
        Loaded += (_, _) => StartScanner(settings);
        Closed += (_, _) => StopScanner();
    }

    private void StartScanner(AppSettings settings)
    {
        var gs = ScannerGsSettings.FromAppSettings(settings);
        _scanner = new RawInputScannerService();
        _scanner.ConfigureGsMapping(gs);
        _scanner.KeyCaptured += OnKeyCaptured;
        _scanner.BarcodeReceived += OnBarcode;
        _scanner.Attach(this,
            settings.ScannerDevicePath,
            wizardMode: string.IsNullOrWhiteSpace(settings.ScannerDevicePath),
            gs);
        Focus();
    }

    private void StopScanner()
    {
        if (_scanner == null)
            return;

        _scanner.KeyCaptured -= OnKeyCaptured;
        _scanner.BarcodeReceived -= OnBarcode;
        _scanner.Stop();
        _scanner = null;
    }

    private void OnKeyCaptured(object? sender, RawInputKeyEvent e)
    {
        Dispatcher.Invoke(() =>
        {
            _liveLog.AppendLine(e.ToLogLine());
            KeysLogText.Text = _liveLog.ToString();
            KeysLogText.ScrollToEnd();
        });
    }

    private void OnBarcode(object? sender, string raw)
    {
        Dispatcher.Invoke(() =>
        {
            var diag = RawInputScannerService.LastScanDiagnostics;
            var display = raw.Replace("\u001D", "[GS]");
            BarcodeText.Text = display;

            if (diag != null)
                HexDumpText.Text = diag.KeysHexDump;

            var parse = _parser.Parse(raw);
            var gsCount = raw.Count(c => c == '\u001D');
            var code = parse.Code;
            var has91 = code?.VerificationKey != null;
            var has92 = code?.VerificationCode != null;
            var isShort = code?.CodeType == MarkingCodeType.Short;

            SummaryText.Text =
                $"Длина: {raw.Length} | GS в буфере: {gsCount} | GS восстановлено картой: {diag?.GsRestoredCount ?? 0} | " +
                $"Тип: {(code?.CodeType.ToString() ?? "-")} | AI93: {(code?.AdditionalField93 != null ? "да" : "нет")} | " +
                $"AI91: {(has91 ? "да" : "нет")} | AI92: {(has92 ? "да" : "нет")} | " +
                $"Разбор: {(parse.IsValid ? "OK" : parse.ErrorMessage)}";

            if (parse.IsValid && isShort)
            {
                GsHintText.Text =
                    "Короткий код маркировки (~30 байт). AI 91/92 в матрице отсутствуют - это нормально, не обрезка HID.";
                GsHintText.Foreground = System.Windows.Media.Brushes.LightGreen;
            }
            else if (gsCount == 0)
            {
                GsHintText.Text =
                    "Разделитель GS (0x1D) не получен. Включите в меню сканера \"GS separator\" / \"FNC1\" / \"Transmit GS\" " +
                    "или переключите на Virtual COM. Для полного кода (~80 байт) без GS данные могут быть обрезаны прошивкой.";
            }
            else if (!has91 || !has92)
            {
                GsHintText.Text =
                    "GS есть, но AI 91/92 не распознаны. Если это короткая матрица - код может быть полным. " +
                    "Для печати полного дубля нужен код ~80+ байт со сканера/COM.";
            }
            else
            {
                GsHintText.Text = "Полный код с GS и криптохвостом получен.";
                GsHintText.Foreground = System.Windows.Media.Brushes.LightGreen;
            }

            _liveLog.Clear();
        });
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();
        sb.AppendLine(SummaryText.Text);
        sb.AppendLine(GsHintText.Text);
        sb.AppendLine();
        sb.AppendLine("=== HEX ===");
        sb.AppendLine(HexDumpText.Text);
        sb.AppendLine();
        sb.AppendLine("=== KEYS ===");
        sb.AppendLine(KeysLogText.Text);
        sb.AppendLine();
        sb.AppendLine("=== BARCODE ===");
        sb.AppendLine(BarcodeText.Text);
        Clipboard.SetText(sb.ToString());
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
