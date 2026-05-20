using System.Windows;
using System.Windows.Controls;
using DoubleMark.Desktop.Services;
using DoubleMark.Desktop.Settings;
using MessageBox = System.Windows.MessageBox;

namespace DoubleMark.Desktop;

public partial class ScannerSettingsWindow : Window
{
    private readonly AppSettings _working;
    private IReadOnlyList<HidDeviceInfo> _devices = Array.Empty<HidDeviceInfo>();

    public AppSettings? ResultSettings { get; private set; }

    public ScannerSettingsWindow(AppSettings current)
    {
        _working = CloneSettings(current);
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ModeCombo.SelectedIndex = _working.ScannerMode switch
        {
            ScannerMode.Com => 1,
            ScannerMode.Hid => 2,
            ScannerMode.RawInput => 2,
            _ => 0
        };
        AutoBindHidCheck.IsChecked = _working.ScannerAutoBindHid;
        UpdateModePanels();
        RefreshHidDevices();
        StatusText.Text = string.Empty;
    }

    private void OnModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
            return;

        UpdateModePanels();
    }

    private void UpdateModePanels()
    {
        var mode = GetSelectedMode();
        var showHid = mode is ScannerMode.Hid or ScannerMode.Auto;
        HidPanel.Visibility = showHid ? Visibility.Visible : Visibility.Collapsed;
        ComHintPanel.Visibility = Visibility.Visible;

        var port = string.IsNullOrWhiteSpace(_working.ComPort) ? "не выбран" : _working.ComPort;
        ComHintText.Text = mode switch
        {
            ScannerMode.Auto =>
                $"Авто: слушаем COM ({port}) и HID. Порт COM можно сменить на главной панели.",
            ScannerMode.Com =>
                $"Только COM. Порт: {port}. Подключение — «Подключить» на главной панели.",
            ScannerMode.Hid =>
                "Только HID. Выберите устройство ниже или используйте мастер определения.",
            _ => $"COM-порт: {port}."
        };
    }

    private ScannerMode GetSelectedMode() => ModeCombo.SelectedIndex switch
    {
        1 => ScannerMode.Com,
        2 => ScannerMode.Hid,
        _ => ScannerMode.Auto
    };

    private void OnRefreshHidClick(object sender, RoutedEventArgs e) => RefreshHidDevices();

    private void RefreshHidDevices()
    {
        _devices = HidDeviceDiscoveryService.EnumerateKeyboardDevices();
        HidDevicesCombo.ItemsSource = _devices;

        if (_devices.Count == 0)
        {
            HidHintText.Text = "HID-устройства не найдены. Проверьте подключение сканера.";
            HidDevicesCombo.SelectedIndex = -1;
            return;
        }

        HidHintText.Text = $"Найдено устройств: {_devices.Count}";
        var path = _working.EffectiveHidDevicePath;
        var selected = _devices.FirstOrDefault(d =>
            !string.IsNullOrWhiteSpace(path)
            && d.DevicePath.Equals(path, StringComparison.OrdinalIgnoreCase));
        HidDevicesCombo.SelectedItem = selected ?? _devices[0];
    }

    private void OnDetectHidClick(object sender, RoutedEventArgs e)
    {
        var wizard = new ScannerSetupWindow(_working) { Owner = this };
        if (wizard.ShowDialog() == true && wizard.ResultSettings != null)
        {
            _working.ScannerMode = ScannerMode.Hid;
            _working.SelectedHidDeviceId = wizard.ResultSettings.EffectiveHidDevicePath;
            _working.ScannerDevicePath = _working.SelectedHidDeviceId;
            _working.NormalizeScannerFields();
            RefreshHidDevices();
            StatusText.Text = "Сканер определён мастером. Нажмите «Применить».";
            StatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
        }
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        var mode = GetSelectedMode();
        string? hidPath = null;

        if (mode is ScannerMode.Hid or ScannerMode.Auto)
        {
            if (HidDevicesCombo.SelectedItem is HidDeviceInfo hid)
                hidPath = hid.DevicePath;
        }

        _working.ScannerAutoBindHid = AutoBindHidCheck.IsChecked == true;

        var result = ScannerSettingsApplier.Apply(_working, mode, hidPath, rawInputDevicePath: null);

        StatusText.Text = result.UserMessage;
        StatusText.Foreground = result.Success
            ? System.Windows.Media.Brushes.LightGreen
            : System.Windows.Media.Brushes.OrangeRed;

        if (!result.Success)
        {
            MessageBox.Show(
                result.UserMessage + (result.OpenLogsHint
                    ? "\n\nПодробности: %AppData%\\DoubleMark\\logs\\"
                    : ""),
                "Настройки сканера",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        ResultSettings = _working;
        DialogResult = true;
        Close();
    }

    private void OnOpenLogsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "\"" + LoggingService.LogsFolderPath + "\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            LoggingService.Error("Scanner.Settings", "Open logs failed", ex);
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static AppSettings CloneSettings(AppSettings source) =>
        new()
        {
            ComPort = source.ComPort,
            ComBaudRate = source.ComBaudRate,
            ScannerDevicePath = source.ScannerDevicePath,
            SelectedHidDeviceId = source.SelectedHidDeviceId,
            SelectedRawInputDeviceId = source.SelectedRawInputDeviceId,
            ScannerMode = source.ScannerMode,
            AutoSaveExports = source.AutoSaveExports,
            ExportDirectory = source.ExportDirectory,
            PrintMode = source.PrintMode,
            AutoPrintEnabled = source.AutoPrintEnabled,
            PrinterName = source.PrinterName,
            PrintCopies = source.PrintCopies,
            PrintWithoutConfirmation = source.PrintWithoutConfirmation,
            PrintDelayMs = source.PrintDelayMs,
            DuplicatePrintBlockSeconds = source.DuplicatePrintBlockSeconds,
            SavePrintFileBeforePrint = source.SavePrintFileBeforePrint,
            PrintDirectory = source.PrintDirectory,
            DefaultPrintTemplateName = source.DefaultPrintTemplateName,
            ScannerGsMappingMode = source.ScannerGsMappingMode,
            ScannerVisibleGsChar = source.ScannerVisibleGsChar,
            ScannerCustomGsVkey = source.ScannerCustomGsVkey,
            ScannerCustomGsMakeCode = source.ScannerCustomGsMakeCode,
            ScannerCustomGsRequiresCtrl = source.ScannerCustomGsRequiresCtrl,
            ScannerCustomGsRequiresShift = source.ScannerCustomGsRequiresShift,
            ScannerCustomGsRequiresAlt = source.ScannerCustomGsRequiresAlt,
            ScannerAutoBindHid = source.ScannerAutoBindHid
        };
}
