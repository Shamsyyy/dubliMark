using System.Windows;
using System.Windows.Controls;

namespace DubliMark.Desktop.Views;

public partial class ScanView : UserControl
{
    public event RoutedEventHandler? SetupScannerRequested;
    public event RoutedEventHandler? HidDiagnosticsRequested;

    public ScanView() => InitializeComponent();

    private void OnSetupScannerProxyClick(object sender, RoutedEventArgs e) =>
        SetupScannerRequested?.Invoke(sender, e);

    private void OnHidDiagnosticsProxyClick(object sender, RoutedEventArgs e) =>
        HidDiagnosticsRequested?.Invoke(sender, e);
}
