using System.Windows;
using System.Windows.Controls;
using DoubleMark.Desktop.Services.Crpt;
using DoubleMark.Desktop.ViewModels.Crpt;
using Microsoft.Win32;

namespace DoubleMark.Desktop.Views;

public partial class CrptCatalogView : UserControl
{
    public event Action<string>? OrderCodesRequested;
    public event Action? SettingsRequested;

    public CrptCatalogView(CrptCatalogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.OrderCodesRequested += gtin => OrderCodesRequested?.Invoke(gtin);
        viewModel.SettingsRequested += () => SettingsRequested?.Invoke();
        viewModel.SaveCsvFileRequested += SaveCsvFile;
        Loaded += (_, _) => viewModel.Load();
    }

    private void OnOpenSettingsClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is CrptCatalogViewModel viewModel)
            viewModel.OpenSettings();
    }

    private static bool SaveCsvFile(string csv)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Экспорт каталога CRPT",
            Filter = "CSV (*.csv)|*.csv|Все файлы (*.*)|*.*",
            FileName = $"crpt-catalog-{DateTime.Now:yyyy-MM-dd}.csv",
            DefaultExt = ".csv",
        };

        if (dlg.ShowDialog() != true)
            return false;

        CrptCatalogCsvExporter.WriteUtf8Bom(dlg.FileName, csv);
        return true;
    }
}
