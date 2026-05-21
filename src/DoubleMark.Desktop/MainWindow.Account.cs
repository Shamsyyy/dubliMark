using System.Diagnostics;
using System.Windows;
using DoubleMark.Desktop.Services;
using DoubleMark.Desktop.Services.Account;

namespace DoubleMark.Desktop;

public partial class MainWindow
{
    private const string DoubleMarkSite = "https://shamsyyy.github.io/doublemarksite/";
    private const string DoubleMarkRegisterUrl = "https://shamsyyy.github.io/doublemarksite/register";
    private const string DoubleMarkAccountUrl = "https://shamsyyy.github.io/doublemarksite/account";
    private const string DoubleMarkPricingUrl = DoubleMarkSite;

    private SupabaseClientFactory _supabaseClientFactory = null!;
    private AuthService _authService = null!;
    private ProfileService _profileService = null!;
    private SubscriptionService _subscriptionService = null!;
    private PaymentService _paymentService = null!;
    private DeviceService _deviceService = null!;
    private AccountService _accountService = null!;
    private AccountSnapshot _accountSnapshot = new(
        null,
        null,
        SubscriptionStatus.Missing,
        Array.Empty<AccountPayment>(),
        Array.Empty<AccountDevice>(),
        "Загружаем аккаунт...");

    private void InitializeAccountServices()
    {
        _supabaseClientFactory = new SupabaseClientFactory();
        _authService = new AuthService(_supabaseClientFactory);
        _profileService = new ProfileService(_supabaseClientFactory);
        _subscriptionService = new SubscriptionService(_supabaseClientFactory);
        _paymentService = new PaymentService(_subscriptionService);
        _deviceService = new DeviceService(_supabaseClientFactory);
        _accountService = new AccountService(
            _authService,
            _profileService,
            _subscriptionService,
            _paymentService,
            _deviceService,
            _supabaseClientFactory);
    }

    private async Task RestoreAccountOnStartupAsync()
    {
        UpdateAccountShell("Загружаем аккаунт...");
        _accountSnapshot = await _accountService.RestoreAccount();
        ApplyAccountSnapshot();
        if (_accountSnapshot.User != null)
            await LoadUserCloudDataAsync();
        else
            ClearUserCloudData();

        if (_accountSnapshot.User == null)
            ShowLogin(_accountSnapshot.Error);
        else if (!_accountSnapshot.Subscription.IsActive)
            NavigateTo(GetAccountView(), NavAccountButton, "Личный кабинет DoubleMark");
        else
            NavigateTo(_dashboardPage!, NavDashboardButton, "Главная панель");
    }

    private async void OnLoginSignInRequested(object? sender, (string Email, string Password) credentials)
    {
        if (!_accountService.IsConfigured)
        {
            _loginView?.SetStatus("Не настроено подключение к серверу DoubleMark. Проверьте SUPABASE_URL и SUPABASE_ANON_KEY.", canSignIn: false);
            return;
        }

        if (string.IsNullOrWhiteSpace(credentials.Email) || string.IsNullOrWhiteSpace(credentials.Password))
        {
            _loginView?.SetStatus("Введите email и пароль.");
            return;
        }

        try
        {
            _loginView?.SetStatus("Проверяем аккаунт и подписку...", isLoading: true);
            _accountSnapshot = await _accountService.SignIn(credentials.Email, credentials.Password);
            ApplyAccountSnapshot();
            if (_accountSnapshot.User != null)
                await LoadUserCloudDataAsync();

            if (_accountSnapshot.User == null || !_accountSnapshot.Subscription.IsActive)
                NavigateTo(GetAccountView(), NavAccountButton, "Личный кабинет DoubleMark");
            else
                NavigateTo(_dashboardPage!, NavDashboardButton, "Главная панель");
        }
        catch (Exception ex)
        {
            _loginView?.SetStatus(FriendlyAccountError(ex), isLoading: false);
        }
    }

    private async void OnAccountRefreshRequested(object? sender, RoutedEventArgs e)
    {
        await RefreshAccountSnapshotAsync(showToast: true);
    }

    private async void OnAccountSignOutRequested(object? sender, RoutedEventArgs e)
    {
        await SignOutAndShowLogin();
    }

    private async void OnAccountSettingsRequested(object? sender, RoutedEventArgs e)
    {
        if (_accountSnapshot.User == null)
        {
            ShowLogin("Сначала войдите в аккаунт DoubleMark.");
            return;
        }

        var window = new AccountSettingsWindow(_accountSnapshot.Profile) { Owner = this };
        window.ResetPasswordRequested += (_, _) => OpenAccountSite();
        if (window.ShowDialog() != true || window.Result == null)
            return;

        try
        {
            await _profileService.UpdateProfile(_accountSnapshot.User.Id, window.Result);
            await RefreshAccountSnapshotAsync(showToast: false);
            ShowToast("Профиль DoubleMark обновлен", ToastKind.Success);
        }
        catch (Exception ex)
        {
            ShowToast("Ошибка обновления профиля: " + FriendlyAccountError(ex), ToastKind.Error);
        }
    }

    private async Task RefreshAccountSnapshotAsync(bool showToast)
    {
        _accountSnapshot = await _accountService.Refresh();
        ApplyAccountSnapshot();
        if (showToast)
            ShowToast(_accountSnapshot.Error ?? "Данные аккаунта DoubleMark обновлены", _accountSnapshot.Error == null ? ToastKind.Success : ToastKind.Warning);
    }

    private async Task SignOutAndShowLogin()
    {
        await _accountService.SignOut();
        _accountSnapshot = new AccountSnapshot(null, null, SubscriptionStatus.Missing, Array.Empty<AccountPayment>(), Array.Empty<AccountDevice>());
        ClearUserCloudData();
        ApplyAccountSnapshot();
        SyncConnectedViews();
        ShowLogin("Вы вышли из аккаунта DoubleMark.");
    }

    private void ShowLogin(string? status)
    {
        var login = GetLoginView();
        var canSignIn = _accountService.IsConfigured;
        login.SetStatus(
            status ?? (canSignIn
                ? "Введите email и пароль."
                : "Не настроено подключение к серверу DoubleMark. Проверьте SUPABASE_URL и SUPABASE_ANON_KEY."),
            canSignIn: canSignIn);
        PageTitleText.Text = "Вход в DoubleMark";
        PageHost.Content = login;
        SetActiveNav(NavAccountButton);
    }

    private void ApplyAccountSnapshot()
    {
        UpdateAccountShell(_accountSnapshot.Error ?? _accountSnapshot.Subscription.DisplayStatus);
        _accountView?.UpdateState(_accountSnapshot);
    }

    private void UpdateAccountShell(string status)
    {
        var profile = _accountSnapshot.Profile;
        var user = _accountSnapshot.User;
        var title = profile?.Organization ?? user?.Email ?? "Аккаунт DoubleMark";
        var email = user?.Email ?? "Войдите в аккаунт";
        var plan = _accountSnapshot.Subscription.Subscription?.PlanId ?? "—";

        AccountInitialsText.Text = BuildInitials(title, email);
        DashboardAccountInitialsText.Text = AccountInitialsText.Text;
        AccountNameText.Text = title;
        AccountOrgText.Text = status;
        AccountPopupNameText.Text = title;
        AccountPopupEmailText.Text = email;
        AccountPlanButton.Content = "Подписка DoubleMark: " + plan;
        DashboardAccountNameText.Text = title;
        DashboardAccountEmailText.Text = email;
        DashboardAccountOrganizationText.Text = profile?.Organization ?? "—";
        DashboardAccountPlanText.Text = plan;
        DashboardAccountStatusText.Text = _accountSnapshot.Subscription.DisplayStatus;
    }

    private async Task<bool> EnsureSubscriptionForFeatureAsync(string featureName)
    {
        if (!EnsureAppVersionAllowed(featureName))
            return false;

        if (!ProductionGuard.CanUseProtectedFeature())
        {
            ShowToast(ProductionGuard.ProtectedFeatureBlockedMessage, ToastKind.Error);
            return false;
        }

        if (_accountSnapshot.User == null)
        {
            ShowLogin("Сначала войдите в аккаунт DoubleMark.");
            return false;
        }

        if (FeatureAccessRules.CanUsePremiumFeature(_accountSnapshot.Subscription))
            return true;

        if (_accountSnapshot.User != null)
            await RefreshAccountSnapshotAsync(showToast: false);
        if (FeatureAccessRules.CanUsePremiumFeature(_accountSnapshot.Subscription))
            return true;

        var window = new SubscriptionRequiredWindow(featureName) { Owner = this };
        window.ShowDialog();
        switch (window.SelectedAction)
        {
            case SubscriptionRequiredAction.OpenPricing:
                OpenPricing();
                break;
            case SubscriptionRequiredAction.SwitchAccount:
                await SignOutAndShowLogin();
                break;
        }

        return false;
    }

    private void OpenRegister() => OpenUrl(DoubleMarkRegisterUrl);
    private void OpenAccountSite() => OpenUrl(DoubleMarkAccountUrl);
    private void OpenPricing() => OpenUrl(DoubleMarkPricingUrl);

    private static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });

    private static string FriendlyAccountError(Exception ex)
    {
        Debug.WriteLine(ex);
        if (ex.Message.Contains("network", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("socket", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("internet", StringComparison.OrdinalIgnoreCase))
            return "Нет подключения к серверу DoubleMark. Проверьте интернет и попробуйте снова.";

        if (ex.Message.Contains("invalid", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("credentials", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("login", StringComparison.OrdinalIgnoreCase))
            return "Неверный email или пароль.";

        if (ex.Message.Contains("confirm", StringComparison.OrdinalIgnoreCase))
            return "Email не подтвержден. Проверьте почту и подтвердите аккаунт DoubleMark.";

        return "Ошибка входа в DoubleMark. Проверьте данные и попробуйте снова.";
    }

    private static string BuildInitials(string? title, string? fallback)
    {
        var source = !string.IsNullOrWhiteSpace(title) ? title : fallback;
        if (string.IsNullOrWhiteSpace(source))
            return "DM";

        var letters = source.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part[0])
            .Take(2)
            .ToArray();
        return letters.Length == 0 ? "DM" : new string(letters).ToUpperInvariant();
    }
}
