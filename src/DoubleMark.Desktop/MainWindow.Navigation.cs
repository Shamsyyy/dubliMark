using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using DoubleMark.Desktop.Views;

namespace DoubleMark.Desktop;

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
    private LoginView? _loginView;

    private void InitializeNavigation()
    {
        _dashboardPage = PageHost.Content;
        PageHost.Content = GetLoginView();
        PageTitleText.Text = "Вход в DoubleMark";
        SetActiveNav(NavAccountButton);
    }

    private void OnNavigateDashboardClick(object sender, RoutedEventArgs e) =>
        NavigateToProtected(() => _dashboardPage!, NavDashboardButton, "Главная панель");

    private void OnNavigateScanClick(object sender, RoutedEventArgs e) =>
        NavigateToProtected(GetScanView, NavScanButton, "Сканирование");

    private void OnNavigatePrintClick(object sender, RoutedEventArgs e) =>
        NavigateToProtected(GetPrintView, NavPrintButton, "Печать");

    private void OnNavigatePdfPrintClick(object sender, RoutedEventArgs e) =>
        NavigateToProtected(GetPdfPrintView, NavPdfPrintButton, "Печать из PDF");

    private void OnNavigateTemplatesClick(object sender, RoutedEventArgs e) =>
        NavigateToProtected(GetTemplatesView, NavTemplatesButton, "Шаблоны");

    private void OnNavigateHistoryClick(object sender, RoutedEventArgs e) =>
        NavigateToProtected(GetHistoryView, NavHistoryButton, "История");

    private void OnNavigateExportClick(object sender, RoutedEventArgs e) =>
        NavigateToProtected(GetExportView, NavExportButton, "Экспорт");

    private void OnNavigateDiagnosticsClick(object sender, RoutedEventArgs e) =>
        NavigateToProtected(GetDiagnosticsView, NavDiagnosticsButton, "Диагностика");

    private void OnNavigateAccountClick(object sender, RoutedEventArgs e) =>
        NavigateTo(GetAccountView(), NavAccountButton, "Личный кабинет DoubleMark");

    private ScanView GetScanView()
    {
        if (_scanView != null)
            return _scanView;

        _scanView = new ScanView();
        _scanView.ConnectRequested += OnScanViewConnectRequested;
        _scanView.RefreshPortsRequested += OnScanViewRefreshPortsRequested;
        _scanView.SetupScannerRequested += OnSetupScannerClick;
        _scanView.HidDiagnosticsRequested += OnHidDiagnosticsClick;
        _scanView.LoadImageRequested += OnLoadImageClick;
        _scanView.PasteImageRequested += OnPasteImageClick;
        _scanView.ModeSelectionRequested += OnScanViewModeSelectionRequested;
        SyncScannerPageState();
        return _scanView;
    }

    private PrintView GetPrintView()
    {
        if (_printView != null)
            return _printView;

        _printView = new PrintView();
        _printView.PrintLastRequested += OnPrintLastClick;
        _printView.PrintSettingsRequested += OnPrintSettingsClick;
        _printView.OpenPrintFolderRequested += OnOpenPrintFolderClick;
        _printView.TestPrintRequested += OnPrintSettingsClick;
        _printView.AutoPrintChanged += OnPrintViewAutoPrintChanged;
        _printView.PrinterChanged += OnPrintViewPrinterChanged;
        _printView.TemplateChanged += OnPrintViewTemplateChanged;
        _printView.CopiesChanged += OnPrintViewCopiesChanged;
        _printView.RefreshPrintersRequested += OnRefreshPrintersClick;
        SyncPrintPageState();
        return _printView;
    }

    private TemplatesView GetTemplatesView()
    {
        if (_templatesView != null)
            return _templatesView;

        _templatesView = new TemplatesView();
        WireTemplatesViewEvents(_templatesView);
        SyncTemplatesPageState();
        return _templatesView;
    }

    private HistoryView GetHistoryView()
    {
        if (_historyView != null)
            return _historyView;

        _historyView = new HistoryView();
        _historyView.ConfigurePreview(_printTemplateService);
        _historyView.CopyRequested += OnHistoryCopyRequested;
        _historyView.ReprintRequested += OnHistoryReprintRequested;
        _historyView.DeleteRequested += OnHistoryDeleteRequested;
        _historyView.ClearHistoryRequested += OnHistoryClearRequested;
        _historyView.SettingsChanged += OnHistorySettingsChanged;
        _historyView.BrowseFolderRequested += OnHistoryBrowseFolderRequested;
        _historyView.ReloadRequested += OnHistoryReloadRequested;
        _historyView.ExportSelectedRequested += OnHistoryExportSelectedRequested;
        SyncHistorySettingsUi();
        SyncHistoryPageState();
        return _historyView;
    }

    private ExportView GetExportView()
    {
        if (_exportView != null)
            return _exportView;

        _exportView = new ExportView();
        _exportView.ChooseExportFolderRequested += OnChooseExportFolderClick;
        _exportView.OpenExportFolderRequested += OnOpenExportRootFolderClick;
        _exportView.AutoSaveChanged += OnExportViewAutoSaveChanged;
        SyncExportPageState();
        return _exportView;
    }

    private DiagnosticsView GetDiagnosticsView()
    {
        if (_diagnosticsView != null)
            return _diagnosticsView;

        _diagnosticsView = new DiagnosticsView();
        _diagnosticsView.HidDiagnosticsRequested += OnHidDiagnosticsClick;
        _diagnosticsView.ResetSettingsRequested += OnResetSettingsClick;
        _diagnosticsView.OpenLogsRequested += OnOpenLogsFolderClick;
        _diagnosticsView.CopyDiagnosticsRequested += OnCopyDiagnosticsClick;
        _diagnosticsView.RefreshComPortsRequested += OnRefreshClick;
        _diagnosticsView.RefreshPrintersRequested += OnRefreshPrintersClick;
        SyncDiagnosticsPageState();
        return _diagnosticsView;
    }

    private AccountView GetAccountView()
    {
        if (_accountView != null)
            return _accountView;

        _accountView = new AccountView();
        _accountView.ProfileSettingsRequested += OnAccountSettingsRequested;
        _accountView.SignOutRequested += OnAccountSignOutRequested;
        _accountView.RefreshRequested += OnAccountRefreshRequested;
        _accountView.OpenAccountSiteRequested += (_, _) => OpenAccountSite();
        _accountView.OpenPricingRequested += (_, _) => OpenPricing();
        _accountView.ResetPasswordRequested += (_, _) => OpenAccountSite();
        _accountView.CheckUpdatesRequested += OnAccountCheckUpdatesRequested;
        _accountView.DownloadUpdateRequested += OnAccountDownloadUpdateRequested;
        _accountView.OpenDownloadsPageRequested += OnAccountOpenDownloadsPageRequested;
        _accountView.AutoCheckUpdatesChanged += OnAccountAutoCheckUpdatesChanged;
        _accountView.SetAutoCheckUpdates(_settings.AutoCheckUpdates);
        _accountView.UpdateState(_accountSnapshot);
        RefreshAccountUpdateUi();
        return _accountView;
    }

    private LoginView GetLoginView()
    {
        if (_loginView != null)
            return _loginView;

        _loginView = new LoginView();
        _loginView.SignInRequested += OnLoginSignInRequested;
        _loginView.RegisterRequested += (_, _) => OpenRegister();
        _loginView.ResetPasswordRequested += (_, _) => OpenAccountSite();
        return _loginView;
    }

    private void NavigateTo(object content, Button activeButton, string pageTitle)
    {
        PageTitleText.Text = pageTitle;
        SyncConnectedViews();
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

    private void NavigateToProtected(Func<object> contentFactory, Button activeButton, string pageTitle)
    {
        if (!EnsureAppVersionAllowed(pageTitle))
            return;

        if (_accountSnapshot.User == null)
        {
            ShowLogin("Сначала войдите в аккаунт DoubleMark.");
            return;
        }

        if (!_accountSnapshot.Subscription.IsActive)
        {
            NavigateTo(GetAccountView(), NavAccountButton, "Личный кабинет DoubleMark");
            ShowToast("Для работы нужна активная подписка DoubleMark.", ToastKind.Warning);
            return;
        }

        NavigateTo(contentFactory(), activeButton, pageTitle);
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
                     NavPdfPrintButton,
                     NavTemplatesButton,
                     NavHistoryButton,
                     NavExportButton,
                     NavDiagnosticsButton,
                     NavCrptSettingsButton,
                     NavCrptCatalogButton,
                     NavCrptOrdersButton,
                     NavCrptPrintQueueButton,
                     NavAccountButton
                 })
        {
            button.Style = ReferenceEquals(button, activeButton) ? active : inactive;
        }
    }
}
