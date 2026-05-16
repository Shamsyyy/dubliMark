using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using DubliMark.Desktop.Views;

namespace DubliMark.Desktop;

public partial class MainWindow
{
    private object? _dashboardPage;
    private ScanView? _scanView;
    private PrintView? _printView;
    private TemplatesView? _templatesView;
    private HistoryView? _historyView;
    private ExportView? _exportView;
    private DiagnosticsView? _diagnosticsView;
    private AccountView? _accountView;

    private void InitializeNavigation()
    {
        _dashboardPage = PageHost.Content;
        SetActiveNav(NavDashboardButton);
    }

    private void OnNavigateDashboardClick(object sender, RoutedEventArgs e) =>
        NavigateTo(_dashboardPage!, NavDashboardButton, "Главная панель");

    private void OnNavigateScanClick(object sender, RoutedEventArgs e) =>
        NavigateTo(GetScanView(), NavScanButton, "Сканирование");

    private void OnNavigatePrintClick(object sender, RoutedEventArgs e) =>
        NavigateTo(GetPrintView(), NavPrintButton, "Печать");

    private void OnNavigateTemplatesClick(object sender, RoutedEventArgs e) =>
        NavigateTo(GetTemplatesView(), NavTemplatesButton, "Шаблоны");

    private void OnNavigateHistoryClick(object sender, RoutedEventArgs e) =>
        NavigateTo(_historyView ??= new HistoryView(), NavHistoryButton, "История");

    private void OnNavigateExportClick(object sender, RoutedEventArgs e) =>
        NavigateTo(GetExportView(), NavExportButton, "Экспорт");

    private void OnNavigateDiagnosticsClick(object sender, RoutedEventArgs e) =>
        NavigateTo(GetDiagnosticsView(), NavDiagnosticsButton, "Диагностика");

    private void OnNavigateAccountClick(object sender, RoutedEventArgs e) =>
        NavigateTo(_accountView ??= new AccountView(), NavAccountButton, "Личный кабинет");

    private ScanView GetScanView()
    {
        if (_scanView != null)
            return _scanView;

        _scanView = new ScanView();
        _scanView.SetupScannerRequested += OnSetupScannerClick;
        _scanView.HidDiagnosticsRequested += OnHidDiagnosticsClick;
        return _scanView;
    }

    private PrintView GetPrintView()
    {
        if (_printView != null)
            return _printView;

        _printView = new PrintView();
        _printView.PrintLastRequested += OnPrintLastClick;
        _printView.PrintSettingsRequested += OnPrintSettingsClick;
        return _printView;
    }

    private TemplatesView GetTemplatesView()
    {
        if (_templatesView != null)
            return _templatesView;

        _templatesView = new TemplatesView();
        _templatesView.ManageTemplatesRequested += OnPrintTemplatesClick;
        return _templatesView;
    }

    private ExportView GetExportView()
    {
        if (_exportView != null)
            return _exportView;

        _exportView = new ExportView();
        _exportView.ChooseExportFolderRequested += OnChooseExportFolderClick;
        return _exportView;
    }

    private DiagnosticsView GetDiagnosticsView()
    {
        if (_diagnosticsView != null)
            return _diagnosticsView;

        _diagnosticsView = new DiagnosticsView();
        _diagnosticsView.HidDiagnosticsRequested += OnHidDiagnosticsClick;
        _diagnosticsView.ResetSettingsRequested += OnResetSettingsClick;
        return _diagnosticsView;
    }

    private void NavigateTo(object content, Button activeButton, string pageTitle)
    {
        PageTitleText.Text = pageTitle;
        if (ReferenceEquals(PageHost.Content, content))
        {
            SetActiveNav(activeButton);
            return;
        }

        SetActiveNav(activeButton);
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(110));
        fadeOut.Completed += (_, _) =>
        {
            PageHost.Content = content;
            PageHostTranslate.Y = 10;
            PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            PageHostTranslate.BeginAnimation(
                System.Windows.Media.TranslateTransform.YProperty,
                new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
        };
        PageHost.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void SetActiveNav(Button activeButton)
    {
        var inactive = (Style)FindResource("SidebarButton");
        var active = (Style)FindResource("SidebarButtonActive");
        foreach (var button in new[]
                 {
                     NavDashboardButton,
                     NavScanButton,
                     NavPrintButton,
                     NavTemplatesButton,
                     NavHistoryButton,
                     NavExportButton,
                     NavDiagnosticsButton,
                     NavAccountButton
                 })
        {
            button.Style = ReferenceEquals(button, activeButton) ? active : inactive;
        }
    }
}
