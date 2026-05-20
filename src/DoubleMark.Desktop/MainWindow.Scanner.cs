using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using DoubleMark.Core.Models;
using DoubleMark.Core.Parsing;
using DoubleMark.Desktop.Services;
using DoubleMark.Desktop.Settings;

namespace DoubleMark.Desktop;

public partial class MainWindow
{
    private bool _portsRefreshInProgress;
    private string? _lastDiagSource;
    private int _lastDiagLength;
    private int _lastDiagGsCount;
    private bool _lastHasAi01;
    private bool _lastHasAi21;
    private bool _lastHasAi91;
    private bool _lastHasAi92;

    private static readonly int[] BaudRates = [9600, 19200, 38400, 57600, 115200];

    private void InitializeScannerUi()
    {
        if (BaudRateCombo != null)
        {
            BaudRateCombo.ItemsSource = BaudRates.Select(b => b.ToString()).ToList();
            SelectBaudRate(_settings.ComBaudRate);
        }
    }

    private void SelectBaudRate(int baud)
    {
        if (BaudRateCombo == null)
            return;

        var text = (baud > 0 ? baud : 9600).ToString();
        BaudRateCombo.SelectedItem = BaudRateCombo.Items.Cast<object>()
            .FirstOrDefault(i => string.Equals(i.ToString(), text, StringComparison.Ordinal))
            ?? BaudRateCombo.Items.Cast<object>().FirstOrDefault();
    }

    private async void RefreshPorts()
    {
        if (_portsRefreshInProgress)
            return;

        _portsRefreshInProgress = true;
        var previous = PortsCombo.SelectedItem as string;
        LoggingService.Info("Scanner.COM", "Refresh ports started");

        try
        {
            var ports = await Task.Run(SerialScannerService.GetAvailablePorts).ConfigureAwait(true);

            PortsCombo.ItemsSource = ports;
            if (ports.Length > 0)
            {
                LoggingService.Info("Scanner.COM", "Found ports: " + string.Join(", ", ports));
                PortsHintText.Visibility = Visibility.Collapsed;

                if (!string.IsNullOrWhiteSpace(previous)
                    && ports.Contains(previous, StringComparer.OrdinalIgnoreCase))
                    PortsCombo.SelectedItem = previous;
                else if (!string.IsNullOrWhiteSpace(_settings.ComPort)
                         && ports.Contains(_settings.ComPort, StringComparer.OrdinalIgnoreCase))
                    PortsCombo.SelectedItem = _settings.ComPort;
                else
                    PortsCombo.SelectedIndex = 0;
            }
            else
            {
                LoggingService.Warn("Scanner.COM", "No COM ports found");
                PortsHintText.Text =
                    "COM-порты не найдены. Проверьте подключение сканера или используйте HID/Raw Input.";
                PortsHintText.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            LoggingService.Error("Scanner.COM", "Refresh ports failed", ex);
            PortsHintText.Text = "Не удалось обновить список COM-портов.";
            PortsHintText.Visibility = Visibility.Visible;
            SetStatus("Ошибка обновления COM-портов", isError: true);
        }
        finally
        {
            _portsRefreshInProgress = false;
            SyncScannerPageState();
        }
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => RefreshPorts();

    private void OnBaudRateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings || BaudRateCombo?.SelectedItem == null)
            return;

        if (int.TryParse(BaudRateCombo.SelectedItem.ToString(), out var baud) && baud > 0)
        {
            _settings.ComBaudRate = baud;
            _settings.Save();
        }
    }

    private void OnDisconnectClick(object sender, RoutedEventArgs e)
    {
        try
        {
            LoggingService.Info("Scanner", "Disconnect requested");
            _settings.ScannerMode = ScannerMode.Unset;
            _settings.ComPort = null;
            _settings.ScannerDevicePath = null;
            _settings.Save();
            StopScanner();
            ComConnectionPanel.Visibility = Visibility.Visible;
            PortsCombo.Focusable = true;
            RefreshPorts();
            SetStatus("Сканер отключён", isError: false);
            SyncConnectedViews();
        }
        catch (Exception ex)
        {
            LoggingService.Error("Scanner", "Disconnect failed", ex);
            SetStatus("Не удалось отключить сканер", isError: true);
        }
    }

    private void OnConnectClick(object sender, RoutedEventArgs e)
    {
        ApplyScannerSettingsSafe(connectCom: true);
    }

    private void ApplyScannerSettingsSafe(bool connectCom)
    {
        LoggingService.Info("Scanner.Settings", "Apply started");

        try
        {
            if (connectCom)
            {
                var port = PortsCombo.SelectedItem as string;
                if (string.IsNullOrWhiteSpace(port))
                {
                    SetStatus("Порт не выбран", isError: true);
                    LoggingService.Warn("Scanner.Settings", "Apply failed: port not selected");
                    return;
                }

                if (BaudRateCombo?.SelectedItem != null
                    && int.TryParse(BaudRateCombo.SelectedItem.ToString(), out var baud)
                    && baud > 0)
                    _settings.ComBaudRate = baud;

                _settings.ComPort = port;
                if (_settings.ScannerMode != ScannerMode.Auto)
                {
                    _settings.ScannerMode = ScannerMode.Com;
                    _settings.ScannerDevicePath = null;
                }

                LoggingService.Info("Scanner.Settings",
                    $"Mode={_settings.ScannerMode} Port={port} BaudRate={_settings.ComBaudRate}");
            }

            _settings.Save();
            var result = RestartScanner();
            if (!result.Success)
            {
                ErrorText.Text = result.Message;
                SetStatus(result.Message, isError: true);
                ShowScannerSettingsError(result.Message, openLogs: true);
                return;
            }

            if (connectCom && _settings.ScannerMode == ScannerMode.Com)
                ComConnectionPanel.Visibility = Visibility.Collapsed;
            if (connectCom && _settings.ScannerMode == ScannerMode.Com)
                PortsCombo.Focusable = false;

            ErrorText.Text = string.Empty;
            SetStatus(result.Message, isError: false);
            SyncConnectedViews();
            Focus();
        }
        catch (Exception ex)
        {
            LoggingService.Error("Scanner.Settings", "Apply failed", ex);
            ErrorText.Text = ex.Message;
            SetStatus("Ошибка настроек сканера", isError: true);
            ShowScannerSettingsError("Не удалось сохранить настройки сканера.", openLogs: true);
        }
    }

    private ScannerConnectResult RestartScanner()
    {
        StopScanner();
        var advisorMessage = ScannerConnectionAdvisor.Apply(_settings);
        var result = ScannerSourceFactory.TryCreate(this, _settings, out _scanner);

        if (_scanner != null)
            _scanner.BarcodeReceived += OnBarcode;

        if (_scanner is SerialScannerService serial)
            serial.ConnectionLost += OnComConnectionLost;
        else if (_scanner is AutoScannerSource autoSource)
            autoSource.ConnectionLost += OnComConnectionLost;

        if (_settings.ScannerMode == ScannerMode.Com && result.IsConnected)
        {
            ComConnectionPanel.Visibility = Visibility.Collapsed;
            PortsCombo.Focusable = false;
        }
        else if (_settings.ScannerMode == ScannerMode.Auto && result.IsConnected)
        {
            ComConnectionPanel.Visibility = Visibility.Visible;
            PortsCombo.Focusable = true;
        }
        else
        {
            PortsCombo.Focusable = true;
            if (_settings.ScannerMode is ScannerMode.Hid or ScannerMode.RawInput)
                ComConnectionPanel.Visibility = Visibility.Visible;
        }

        UpdateStatusFromSettings();
        LoggingService.Info("Scanner", result.Message);

        if (!string.IsNullOrWhiteSpace(advisorMessage) && result.Success)
            return result with { Message = advisorMessage };

        return result;
    }

    private void OnComConnectionLost(object? sender, string reason)
    {
        Dispatcher.Invoke(() =>
        {
            LoggingService.Warn("Scanner.COM", reason);
            StopScanner();
            ComConnectionPanel.Visibility = Visibility.Visible;
            PortsCombo.Focusable = true;
            RefreshPorts();
            SetStatus(reason, isError: true);
            SyncConnectedViews();
        });
    }

    private void OnSetupScannerClickSafe(object sender, RoutedEventArgs e)
    {
        if (_isScannerSetupInProgress || _setupWindow != null)
            return;

        _isScannerSetupInProgress = true;
        var previousMode = _settings.ScannerMode;
        StopScanner();

        try
        {
            LoggingService.Info("Scanner.Settings", "Settings window opened");
            var dialog = new ScannerSettingsWindow(_settings) { Owner = this };

            if (dialog.ShowDialog() == true && dialog.ResultSettings != null)
            {
                _settings = dialog.ResultSettings;
                _settings.NormalizeScannerFields();
                _settings.Save();

                LoggingService.Info("Scanner.Settings",
                    $"Applied mode={_settings.ScannerMode} previous={previousMode}");

                if (_settings.ScannerMode == ScannerMode.Com)
                {
                    SelectSavedPort();
                    ComConnectionPanel.Visibility = Visibility.Visible;
                    PortsCombo.Focusable = true;
                    SetStatus("Режим COM. Выберите порт и нажмите «Подключить».", isError: false);
                    ErrorText.Text = string.Empty;
                }
                else
                {
                    var result = RestartScanner();
                    if (!result.Success)
                        ShowScannerSettingsError(result.Message, openLogs: true);
                    else
                    {
                        SetStatus(result.Message, isError: false);
                        ErrorText.Text = string.Empty;
                    }
                }

                SyncConnectedViews();
                Focus();
            }
        }
        catch (Exception ex)
        {
            LoggingService.Error("Scanner.Settings", "Settings window failed", ex);
            ShowScannerSettingsError("Не удалось сохранить настройки сканера.", openLogs: true);
        }
        finally
        {
            _isScannerSetupInProgress = false;
            _setupWindow = null;
            if (_scanner == null && _settings.ScannerMode != ScannerMode.Com)
                RestartScanner();
            else if (_scanner == null && _settings.ScannerMode == ScannerMode.Com
                     && !string.IsNullOrWhiteSpace(_settings.ComPort))
                RestartScanner();
        }
    }

    private void ShowScannerSettingsError(string message, bool openLogs)
    {
        SetStatus(message, isError: true);
        ErrorText.Text = message;
        var logsHint = openLogs
            ? "\n\nПодробности в папке логов:\n" + LoggingService.LogsFolderPath
            : "";
        MessageBox.Show(
            message + logsHint,
            "Настройки сканера",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void OnOpenLogsFolderClick(object sender, RoutedEventArgs e) => OpenFolder(LoggingService.LogsFolderPath);

    private void OnCopyDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        var snapshot = BuildDiagnosticsSnapshot();
        Clipboard.SetText(LoggingService.BuildSafeDiagnosticReport(version, snapshot));
        ShowToast("Диагностика скопирована", ToastKind.Success);
    }

    private void OnRefreshPrintersClick(object sender, RoutedEventArgs e)
    {
        LoggingService.Info("Print", "Refresh printers started");
        var printers = MarkPrintService.GetInstalledPrinters();
        LoggingService.Info("Print", printers.Count == 0
            ? "Found printers: (none)"
            : "Found printers: " + string.Join(", ", printers));

        if (!string.IsNullOrWhiteSpace(_settings.PrinterName)
            && !printers.Contains(_settings.PrinterName, StringComparer.OrdinalIgnoreCase))
        {
            LoggingService.Warn("Print", "Printer not found: " + _settings.PrinterName);
            ShowToast("Выбранный принтер не найден — сброшен на системный", ToastKind.Warning);
            _settings.PrinterName = null;
            _settings.Save();
        }

        RefreshPrintSettingsUi();
        SyncConnectedViews();
    }

    private AppSettingsSnapshot BuildDiagnosticsSnapshot()
    {
        var ports = PortsCombo?.Items.OfType<string>().ToList()
                    ?? SerialScannerService.GetAvailablePorts().ToList();

        return new AppSettingsSnapshot(
            ScannerMode: _settings.ScannerMode.ToString(),
            ComPort: _settings.ComPort,
            ComBaudRate: _settings.ComBaudRate,
            ComConnected: _scanner is SerialScannerService { IsOpen: true },
            AvailableComPorts: ports,
            HidConfigured: ScannerSourceFactory.IsHidConfigured(_settings),
            PrinterName: _settings.PrinterName,
            TemplateName: _settings.DefaultPrintTemplateName,
            PrintMode: _settings.PrintMode.ToString(),
            AutoPrintEnabled: _settings.AutoPrintEnabled,
            LastScanSource: _lastDiagSource,
            LastScanLength: _lastDiagLength,
            LastGsCount: _lastDiagGsCount,
            HasAi01: _lastHasAi01,
            HasAi21: _lastHasAi21,
            HasAi91: _lastHasAi91,
            HasAi92: _lastHasAi92);
    }

    private void UpdateScanDiagnostics(string source, string raw, ParseResult result)
    {
        _lastDiagSource = source;
        _lastDiagLength = raw.Length;
        _lastDiagGsCount = Gs1BarcodeEncoding.CountGs(raw);
        var code = result.Code;
        _lastHasAi01 = !string.IsNullOrWhiteSpace(code?.Gtin);
        _lastHasAi21 = !string.IsNullOrWhiteSpace(code?.Serial);
        _lastHasAi91 = !string.IsNullOrWhiteSpace(code?.VerificationKey);
        _lastHasAi92 = !string.IsNullOrWhiteSpace(code?.VerificationCode);
    }
}
