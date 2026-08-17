using System.Windows;
using System.Windows.Controls;
using DoubleMark.Crpt;
using DoubleMark.Desktop.Services.Crpt;
using DoubleMark.Desktop.Settings;
using DoubleMark.Desktop.ViewModels.Crpt;
using DoubleMark.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DoubleMark.Desktop;

/// <summary>
/// CRPT integration navigation and view wiring (spec §11).
/// </summary>
public partial class MainWindow
{
    private CrptSettingsView? _crptSettingsView;
    private CrptCatalogView? _crptCatalogView;
    private CrptOrdersView? _crptOrdersView;
    private CrptPrintQueueView? _crptPrintQueueView;

    private void InitializeCrptIntegration()
    {
        // Views are created lazily via getters; cross-navigation is wired there.
    }

    private void OnNavigateCrptSettingsClick(object sender, RoutedEventArgs e) =>
        NavigateToProtected(GetCrptSettingsView, NavCrptSettingsButton, "Маркировка — настройки");

    private void OnNavigateCrptCatalogClick(object sender, RoutedEventArgs e) =>
        NavigateToProtected(GetCrptCatalogView, NavCrptCatalogButton, "Каталог товаров");

    private void OnNavigateCrptOrdersClick(object sender, RoutedEventArgs e) =>
        NavigateToProtected(GetCrptOrdersView, NavCrptOrdersButton, "Заказы кодов");

    private void OnNavigateCrptPrintQueueClick(object sender, RoutedEventArgs e) =>
        NavigateToProtected(GetCrptPrintQueueView, NavCrptPrintQueueButton, "Очередь печати CRPT");

    private CrptSettingsView GetCrptSettingsView()
    {
        if (_crptSettingsView != null)
            return _crptSettingsView;

        var viewModel = new CrptSettingsViewModel(
            App.Services.GetRequiredService<ICrptSettingsStore>(),
            App.Services.GetRequiredService<ICrptAuthService>(),
            App.Services.GetRequiredService<ICrptCertificateProvider>(),
            App.Services.GetRequiredService<CrptAuthRuntimeState>());

        _crptSettingsView = new CrptSettingsView(viewModel);
        return _crptSettingsView;
    }

    private CrptCatalogView GetCrptCatalogView()
    {
        if (_crptCatalogView != null)
            return _crptCatalogView;

        var viewModel = new CrptCatalogViewModel(
            App.Services.GetRequiredService<ICrptProductCatalogStore>(),
            App.Services.GetRequiredService<ICrptCatalogSyncService>(),
            App.Services.GetRequiredService<ICrptSettingsStore>(),
            App.Services.GetRequiredService<ICrptCertificateProvider>(),
            App.Services.GetRequiredService<ICrptAuthService>());

        _crptCatalogView = new CrptCatalogView(viewModel);
        _crptCatalogView.OrderCodesRequested += gtin =>
        {
            var ordersView = GetCrptOrdersView();
            ordersView.SetPreselectedGtin(gtin);
            NavigateToProtected(GetCrptOrdersView, NavCrptOrdersButton, "Заказы кодов");
        };
        _crptCatalogView.SettingsRequested += () =>
            NavigateToProtected(GetCrptSettingsView, NavCrptSettingsButton, "Маркировка — настройки");

        return _crptCatalogView;
    }

    private CrptOrdersView GetCrptOrdersView()
    {
        if (_crptOrdersView != null)
            return _crptOrdersView;

        var viewModel = new CrptOrdersViewModel(
            App.Services.GetRequiredService<ICrptProductCatalogStore>(),
            App.Services.GetRequiredService<ICrptSuzService>(),
            App.Services.GetRequiredService<CrptOrderRepository>());

        _crptOrdersView = new CrptOrdersView(viewModel);
        _crptOrdersView.OpenPrintQueueRequested += orderId =>
        {
            var queueView = GetCrptPrintQueueView();
            queueView.SetPreselectedOrder(orderId);
            NavigateToProtected(GetCrptPrintQueueView, NavCrptPrintQueueButton, "Очередь печати CRPT");
        };

        return _crptOrdersView;
    }

    private CrptPrintQueueView GetCrptPrintQueueView()
    {
        if (_crptPrintQueueView != null)
            return _crptPrintQueueView;

        var viewModel = new CrptPrintQueueViewModel(
            App.Services.GetRequiredService<CrptOrderRepository>(),
            App.Services.GetRequiredService<ICrptProductCatalogStore>(),
            App.Services.GetRequiredService<ICrptPrintService>(),
            App.Services.GetRequiredService<ICrptGisMtService>());

        _crptPrintQueueView = new CrptPrintQueueView(viewModel);
        return _crptPrintQueueView;
    }
}
