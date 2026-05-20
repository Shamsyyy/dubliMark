using System.Windows;
using DoubleMark.Desktop.Services;
using DoubleMark.Desktop.Settings;
using MessageBox = System.Windows.MessageBox;

namespace DoubleMark.Desktop;

public partial class ScannerSetupWindow : Window
{
    private readonly AppSettings _baseSettings;
    private RawInputScannerService? _scanner;
    private string? _detectedDevicePath;

    public AppSettings? ResultSettings { get; private set; }

    public ScannerSetupWindow(AppSettings baseSettings)
    {
        _baseSettings = baseSettings;
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            LoggingService.Info("Scanner.Settings", "HID setup window opened");
            var gs = ScannerGsSettings.FromAppSettings(_baseSettings);
            _scanner = new RawInputScannerService();
            _scanner.ScanCompleted += OnScanCompleted;
            _scanner.AttachWhenReady(this, scannerDevicePath: null, wizardMode: true, gs);
            Focus();
        }
        catch (Exception ex)
        {
            LoggingService.Error("Scanner.Settings", "HID setup attach failed", ex);
            StatusText.Text = "Не удалось запустить HID-диагностику. Проверьте подключение сканера.";
            StatusText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            MessageBox.Show(
                "Не удалось применить настройки сканера. Проверьте подключение и попробуйте снова.",
                "Настройки сканера",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnScanCompleted(object? sender, RawInputScanEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (e.IsFastScan && !string.IsNullOrWhiteSpace(e.DevicePath))
            {
                _detectedDevicePath = e.DevicePath;
                StatusText.Text = $"Сканер обнаружен\n{e.DevicePath}\n(средний интервал: {e.AverageIntervalMs:F0} мс)";
                StatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
                DoneButton.IsEnabled = true;
            }
            else
            {
                StatusText.Text = "Сканирование слишком медленное или устройство не определено.\nПовторите сканирование сканером, не с клавиатуры.";
                StatusText.Foreground = System.Windows.Media.Brushes.Orange;
            }
        });
    }

    private void OnDoneClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_detectedDevicePath))
            return;

        ResultSettings = CloneFromBase();
        ResultSettings.ScannerDevicePath = _detectedDevicePath;
        ResultSettings.SelectedHidDeviceId = _detectedDevicePath;
        ResultSettings.ScannerMode = ScannerMode.Hid;
        DialogResult = true;
        Close();
    }

    private AppSettings CloneFromBase() =>
        new()
        {
            ComPort = _baseSettings.ComPort,
            ComBaudRate = _baseSettings.ComBaudRate,
            ScannerDevicePath = _baseSettings.ScannerDevicePath,
            SelectedHidDeviceId = _baseSettings.SelectedHidDeviceId,
            SelectedRawInputDeviceId = _baseSettings.SelectedRawInputDeviceId,
            ScannerMode = _baseSettings.ScannerMode,
            AutoSaveExports = _baseSettings.AutoSaveExports,
            ExportDirectory = _baseSettings.ExportDirectory,
            PrintMode = _baseSettings.PrintMode,
            AutoPrintEnabled = _baseSettings.AutoPrintEnabled,
            PrinterName = _baseSettings.PrinterName,
            PrintCopies = _baseSettings.PrintCopies,
            PrintWithoutConfirmation = _baseSettings.PrintWithoutConfirmation,
            PrintDelayMs = _baseSettings.PrintDelayMs,
            DuplicatePrintBlockSeconds = _baseSettings.DuplicatePrintBlockSeconds,
            SavePrintFileBeforePrint = _baseSettings.SavePrintFileBeforePrint,
            PrintDirectory = _baseSettings.PrintDirectory,
            DefaultPrintTemplateName = _baseSettings.DefaultPrintTemplateName,
            ScannerGsMappingMode = _baseSettings.ScannerGsMappingMode,
            ScannerVisibleGsChar = _baseSettings.ScannerVisibleGsChar,
            ScannerCustomGsVkey = _baseSettings.ScannerCustomGsVkey,
            ScannerCustomGsMakeCode = _baseSettings.ScannerCustomGsMakeCode,
            ScannerCustomGsRequiresCtrl = _baseSettings.ScannerCustomGsRequiresCtrl,
            ScannerCustomGsRequiresShift = _baseSettings.ScannerCustomGsRequiresShift,
            ScannerCustomGsRequiresAlt = _baseSettings.ScannerCustomGsRequiresAlt
        };

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_scanner != null)
        {
            _scanner.ScanCompleted -= OnScanCompleted;
            _scanner.Stop();
            _scanner = null;
        }
    }
}
