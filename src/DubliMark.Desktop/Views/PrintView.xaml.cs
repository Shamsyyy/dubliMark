using System.Windows;
using System.Windows.Controls;

namespace DubliMark.Desktop.Views;

public partial class PrintView : UserControl
{
    public event RoutedEventHandler? PrintLastRequested;
    public event RoutedEventHandler? PrintSettingsRequested;

    public PrintView() => InitializeComponent();

    private void OnPrintLastProxyClick(object sender, RoutedEventArgs e) =>
        PrintLastRequested?.Invoke(sender, e);

    private void OnPrintSettingsProxyClick(object sender, RoutedEventArgs e) =>
        PrintSettingsRequested?.Invoke(sender, e);
}
