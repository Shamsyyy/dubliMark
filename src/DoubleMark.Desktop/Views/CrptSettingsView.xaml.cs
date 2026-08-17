using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using DoubleMark.Crpt;
using DoubleMark.Desktop.ViewModels.Crpt;

namespace DoubleMark.Desktop.Views;

public partial class CrptSettingsView : UserControl
{
    public CrptSettingsView(CrptSettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => viewModel.Load();
    }

    private void OnSyncProductGroupsClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is CrptSettingsViewModel viewModel)
            viewModel.SyncTemplateDefaultsFromProductGroups();
    }

    private void OnTrueApiDocsClick(object sender, RoutedEventArgs e) =>
        OpenDocumentationUrl(CrptDocumentationLinks.TrueApiDocs);

    private void OnNationalCatalogDocsClick(object sender, RoutedEventArgs e) =>
        OpenDocumentationUrl(CrptDocumentationLinks.NationalCatalogApiDocs);

    private void OnKnowledgeBaseClick(object sender, RoutedEventArgs e) =>
        OpenDocumentationUrl(CrptDocumentationLinks.MarkirovkaKnowledgeBase);

    private static void OpenDocumentationUrl(string url) =>
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
}
