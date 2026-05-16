using System.Windows;
using System.Windows.Controls;

namespace DubliMark.Desktop.Views;

public partial class ExportView : UserControl
{
    public event RoutedEventHandler? ChooseExportFolderRequested;

    public ExportView() => InitializeComponent();

    private void OnChooseExportProxyClick(object sender, RoutedEventArgs e) =>
        ChooseExportFolderRequested?.Invoke(sender, e);
}
