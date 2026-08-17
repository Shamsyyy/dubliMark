using System.Windows.Controls;
using DoubleMark.Desktop.ViewModels.Crpt;

namespace DoubleMark.Desktop.Views;

public partial class CrptOrdersView : UserControl
{
    public event Action<string>? OpenPrintQueueRequested;

    public CrptOrdersView(CrptOrdersViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.OpenPrintQueueRequested += orderId => OpenPrintQueueRequested?.Invoke(orderId);
        Loaded += (_, _) => viewModel.Load();
    }

    public void SetPreselectedGtin(string gtin) =>
        ((CrptOrdersViewModel)DataContext).SetPreselectedGtin(gtin);
}
