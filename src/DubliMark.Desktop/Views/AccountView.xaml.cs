using System.Windows;
using System.Windows.Controls;

namespace DubliMark.Desktop.Views;

public partial class AccountView : UserControl
{
    public event RoutedEventHandler? ProfileSettingsRequested;
    public event RoutedEventHandler? SignOutRequested;

    public AccountView() => InitializeComponent();

    private void OnProfileSettingsClick(object sender, RoutedEventArgs e) =>
        ProfileSettingsRequested?.Invoke(sender, e);

    private void OnSignOutClick(object sender, RoutedEventArgs e) =>
        SignOutRequested?.Invoke(sender, e);
}
