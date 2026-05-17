using System.Windows;
using System.Windows.Input;

namespace DoubleMark.Desktop;

public partial class MainWindow
{
    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleWindowState();
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnMaximizeRestoreClick(object sender, RoutedEventArgs e) =>
        ToggleWindowState();

    private void OnCloseClick(object sender, RoutedEventArgs e) =>
        Close();

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        MaximizeRestoreButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
    }
}
