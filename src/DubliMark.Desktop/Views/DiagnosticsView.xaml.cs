using System.Windows;
using System.Windows.Controls;

namespace DubliMark.Desktop.Views;

public partial class DiagnosticsView : UserControl
{
    public event RoutedEventHandler? HidDiagnosticsRequested;
    public event RoutedEventHandler? ResetSettingsRequested;

    public DiagnosticsView() => InitializeComponent();

    private void OnHidDiagnosticsProxyClick(object sender, RoutedEventArgs e) =>
        HidDiagnosticsRequested?.Invoke(sender, e);

    private void OnResetProxyClick(object sender, RoutedEventArgs e) =>
        ResetSettingsRequested?.Invoke(sender, e);
}
