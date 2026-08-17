using System.Windows.Controls;
using DoubleMark.Desktop.ViewModels.Crpt;

namespace DoubleMark.Desktop.Views;

public partial class CrptPrintQueueView : UserControl
{
    public CrptPrintQueueView(CrptPrintQueueViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => viewModel.Load();
    }

    public void SetPreselectedOrder(string orderLocalId) =>
        ((CrptPrintQueueViewModel)DataContext).SetPreselectedOrder(orderLocalId);
}
