using System.Windows;
using System.Windows.Controls;

namespace DubliMark.Desktop.Views;

public partial class TemplatesView : UserControl
{
    public event RoutedEventHandler? ManageTemplatesRequested;

    public TemplatesView() => InitializeComponent();

    private void OnManageTemplatesProxyClick(object sender, RoutedEventArgs e) =>
        ManageTemplatesRequested?.Invoke(sender, e);
}
