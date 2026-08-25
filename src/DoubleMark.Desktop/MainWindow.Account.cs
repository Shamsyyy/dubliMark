using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using DoubleMark.Desktop.Services;
using DoubleMark.Desktop.Services.Account;
using DoubleMark.Desktop.Services.Api;
using DoubleMark.Desktop.Settings;

namespace DoubleMark.Desktop;

public partial class MainWindow
{
    private const string DoubleMarkSite = "https://doublemark.ru/";
    private const string DoubleMarkRegisterUrl = "https://doublemark.ru/register";
    private const string DoubleMarkAccountUrl = "https://doublemark.ru/account";
    private const string DoubleMarkPricingUrl = "https://doublemark.ru/pricing";
    private const string DoubleMarkResetPasswordUrl = "https://doublemark.ru/reset-password";

    private BackendConfig _backendConfig = null!;
    private DoubleMarkApiClient? _apiClient;
    private AuthService _authService = null!;
    private LocalApiProfileService? _localProfileService;
    private IAccountPortal _accountService = null!;
    private AccountSnapshot _accountSnapshot = new(
        null,
        null,
        SubscriptionStatus.Missing,
        Array.Empty<AccountPayment>(),
        Array.Empty<AccountDevice>(),
        "Загружаем аккаунт...");

    private void InitializeAccountServices()
    {
        _backendConfig = BackendConfigLoader.Load();
        _apiClient = new DoubleMarkApiClient(_backendConfig.ApiBaseUrl);
        _authService = new AuthService(new LocalApiAuthGateway(_apiClient));
        _localProfileService = new LocalApiProfileService(_apiClient);
        _accountService = new LocalApiAccountService(
            _authService,
            _localProfileService,
            new LocalApiSubscriptionService(_apiClient),
            new LocalApiPaymentService(_apiClient),
            new LocalApiDeviceService(_apiClient),
            _backendConfig.ApiBaseUrl);
        LoggingService.Info("Backend", "Mode=LocalApi url=" + _backendConfig.ApiBaseUrl);
    }

    private async Task RestoreAccountOnStartupAsync()
    {
        UpdateAccountShell("Загружаем аккаунт...");
        try
        {
            _accountSnapshot = await _accountService.RestoreAccount();
        }
        catch (TimeoutException ex)
        {
            _accountSnapshot = new AccountSnapshot(
                null,
                null,
                SubscriptionStatus.Missing,
                Array.Empty<AccountPayment>(),
                Array.Empty<AccountDevice>(),
                ex.Message);
        }

        ApplyAccountSnapshot();

        if (_accountSnapshot.User == null)
        {
            ClearUserCloudData();
            ShowLogin(_accountSnapshot.Error);
            return;
        }

        if (!_accountSnapshot.Subscription.IsActive)
            NavigateTo(GetAccountView(), NavAccountButton, "Личный кабинет DoubleMark");
        else
            NavigateTo(_dashboardPage!, NavDashboardButton, "Главная панель");

        _ = LoadUserCloudDataSafeAsync();
    }

    private async void OnLoginSignInRequested(object? sender, (string Email, string Password) credentials)
    {
        if (!_accountService.IsConfigured)
        {
            _loginView?.SetStatus(NotConfiguredMessage(), canSignIn: false);
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

            if (_accountSnapshot.User == null || !_accountSnapshot.Subscription.IsActive)
                NavigateTo(GetAccountView(), NavAccountButton, "Личный кабинет DoubleMark");
            else
                NavigateTo(_dashboardPage!, NavDashboardButton, "Главная панель");

            if (_accountSnapshot.User != null)
                _ = LoadUserCloudDataSafeAsync();
        }
        catch (TimeoutException ex)
        {
            _loginView?.SetStatus(ex.Message, isLoading: false);
        }
        catch (Exception ex)
        {
            _loginView?.SetStatus(FriendlyAccountError(ex), isLoading: false);
        }
        finally
        {
            _loginView?.SetLoading(false, canSignIn: _accountService.IsConfigured);
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
        window.ResetPasswordRequested += (_, _) => OpenResetPassword();
        if (window.ShowDialog() != true || window.Result == null)
            return;

        try
        {
            if (_localProfileService != null)
                await _localProfileService.UpdateProfile(_accountSnapshot.User.Id, window.Result);
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
                : NotConfiguredMessage()),
            canSignIn: canSignIn);
        PageTitleText.Text = "Вход в DoubleMark";
        PageHost.Content = login;
        SetActiveNav(NavAccountButton);
    }

    private string NotConfiguredMessage() =>
        "Не настроено подключение к API DoubleMark. Проверьте DOUBLEMARK_API_URL или Backend:ApiBaseUrl.";

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
        AccountNameText.Text = title;
        AccountOrgText.Text = status;
        AccountPopupNameText.Text = title;
        AccountPopupEmailText.Text = email;
        AccountPlanButton.Content = "Подписка DoubleMark: " + plan;
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

    private void OnLoginRegisterRequested(object? sender, RoutedEventArgs e) =>
        OpenRegister();

    private void OpenRegister() => OpenUrl(DoubleMarkRegisterUrl);
    private void OpenAccountSite() => OpenUrl(DoubleMarkAccountUrl);
    private void OpenPricing() => OpenUrl(DoubleMarkPricingUrl);
    private void OpenResetPassword() => OpenUrl(DoubleMarkResetPasswordUrl);

    private static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });

    private string FriendlyAccountError(Exception ex)
    {
        Debug.WriteLine(ex);
        if (ex is TimeoutException)
            return ex.Message;

        var message = ex.Message ?? "";
        var apiDetail = ExtractApiErrorDetail(message);

        if (message.Contains("refused", StringComparison.OrdinalIgnoreCase)
            || message.Contains("actively refused", StringComparison.OrdinalIgnoreCase)
            || message.Contains("No connection could be made", StringComparison.OrdinalIgnoreCase)
            || message.Contains("localhost:5080", StringComparison.OrdinalIgnoreCase))
        {
            var url = _backendConfig.ApiBaseUrl;
            if (url.Contains("localhost", StringComparison.OrdinalIgnoreCase)
                || url.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase))
                return "Нет связи с API DoubleMark. Запустите локальный API или укажите https://api.doublemark.ru.";

            return $"Нет связи с API DoubleMark ({url}). Проверьте интернет и попробуйте снова.";
        }

        if (message.Contains("network", StringComparison.OrdinalIgnoreCase)
            || message.Contains("socket", StringComparison.OrdinalIgnoreCase)
            || message.Contains("internet", StringComparison.OrdinalIgnoreCase)
            || ex is HttpRequestException)
            return string.IsNullOrWhiteSpace(apiDetail)
                ? "Нет подключения к серверу DoubleMark. Проверьте интернет и попробуйте снова."
                : apiDetail;

        if (!string.IsNullOrWhiteSpace(apiDetail)
            && (apiDetail.Contains("подтверд", StringComparison.OrdinalIgnoreCase)
                || apiDetail.Contains("confirm", StringComparison.OrdinalIgnoreCase)))
            return apiDetail;

        if (!string.IsNullOrWhiteSpace(apiDetail)
            && (apiDetail.Contains("Неверный", StringComparison.OrdinalIgnoreCase)
                || apiDetail.Contains("парол", StringComparison.OrdinalIgnoreCase)))
            return apiDetail;

        if (message.Contains("invalid", StringComparison.OrdinalIgnoreCase)
            || message.Contains("credentials", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase)
            || message.Contains("401", StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(apiDetail) ? "Неверный email или пароль." : apiDetail;

        if (message.Contains("confirm", StringComparison.OrdinalIgnoreCase)
            || message.Contains("подтверд", StringComparison.OrdinalIgnoreCase))
            return "Email не подтвержден. Проверьте почту и подтвердите аккаунт DoubleMark.";

        if (message.Contains("403", StringComparison.OrdinalIgnoreCase)
            || message.Contains("лимит устройств", StringComparison.OrdinalIgnoreCase))
        {
            if (message.Contains("лимит", StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(apiDetail)
                    && apiDetail.Contains("лимит", StringComparison.OrdinalIgnoreCase)))
            {
                return "Лимит устройств организации исчерпан. Отключите устройство в личном кабинете на сайте.";
            }

            return "Доступ запрещён для этого действия.";
        }

        if (message.Contains("429", StringComparison.OrdinalIgnoreCase))
            return "Слишком много запросов. Подождите немного и попробуйте снова.";

        if (message.Contains("500", StringComparison.OrdinalIgnoreCase)
            || message.Contains("email не настроена", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Smtp", StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(apiDetail)
                ? "Сервер DoubleMark временно недоступен. Попробуйте позже."
                : apiDetail;

        return string.IsNullOrWhiteSpace(apiDetail)
            ? "Ошибка входа в DoubleMark. Проверьте данные и попробуйте снова."
            : apiDetail;
    }

    private static string? ExtractApiErrorDetail(string message)
    {
        const string marker = "API ";
        var idx = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;

        var colon = message.IndexOf(':', idx);
        if (colon < 0 || colon + 1 >= message.Length)
            return null;

        var detail = message[(colon + 1)..].Trim();
        return string.IsNullOrWhiteSpace(detail) ? null : detail;
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
