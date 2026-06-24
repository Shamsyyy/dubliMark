namespace DoubleMark.Desktop.Services.Account;

public sealed class AccountService
{
    private readonly AuthService _authService;
    private readonly ProfileService _profileService;
    private readonly SubscriptionService _subscriptionService;
    private readonly PaymentService _paymentService;
    private readonly DeviceService _deviceService;
    private readonly SupabaseClientFactory _clientFactory;

    public AccountService(
        AuthService authService,
        ProfileService profileService,
        SubscriptionService subscriptionService,
        PaymentService paymentService,
        DeviceService deviceService,
        SupabaseClientFactory clientFactory)
    {
        _authService = authService;
        _profileService = profileService;
        _subscriptionService = subscriptionService;
        _paymentService = paymentService;
        _deviceService = deviceService;
        _clientFactory = clientFactory;
    }

    public bool IsConfigured => _authService.IsConfigured;

    public async Task<AccountSnapshot> RestoreAccount()
    {
        if (!IsConfigured)
            return Empty("Не настроено подключение к серверу DoubleMark. Проверьте SUPABASE_URL и SUPABASE_ANON_KEY.");

        return await AccountNetworkTimeout.RunAsync(async () =>
        {
            var user = await _authService.RestoreSession();
            return user == null
                ? Empty(null)
                : await LoadAccount(user);
        }, "восстановление аккаунта");
    }

    public async Task<AccountSnapshot> SignIn(string email, string password)
    {
        return await AccountNetworkTimeout.RunAsync(async () =>
        {
            var user = await _authService.SignIn(email, password);
            if (user == null)
                return Empty("Не удалось получить пользователя DoubleMark.");

            return await LoadAccount(user);
        }, "вход в аккаунт");
    }

    public Task SignOut() => _authService.SignOut();

    public async Task<AccountSnapshot> Refresh()
    {
        var user = _authService.GetCurrentUser();
        if (user == null)
            return Empty(null);

        return await AccountNetworkTimeout.RunAsync(() => LoadAccount(user), "обновление аккаунта");
    }

    private async Task<AccountSnapshot> LoadAccount(AccountUser user)
    {
        LogAuthDiagnostics(user);

        AccountProfile? profile = null;
        SubscriptionStatus subscription = SubscriptionStatus.Missing;
        IReadOnlyList<AccountPayment> payments = Array.Empty<AccountPayment>();
        IReadOnlyList<AccountDevice> devices = Array.Empty<AccountDevice>();
        string? criticalError = null;

        try
        {
            profile = await _profileService.GetOrCreateProfile(user);
        }
        catch (Exception ex)
        {
            AccountDiagnostics.LogError("profile query", ex);
            criticalError = FriendlyError(ex);
        }

        try
        {
            subscription = await _subscriptionService.GetSubscriptionStatus(user.Id);
            AccountDiagnostics.Log("subscription status: " + subscription.DisplayStatus + ", isActive=" + subscription.IsActive);
        }
        catch (Exception ex)
        {
            AccountDiagnostics.LogError("subscription query", ex);
            criticalError ??= FriendlyError(ex);
        }

        try
        {
            payments = await _paymentService.GetUserPayments(user.Id);
        }
        catch (Exception ex)
        {
            AccountDiagnostics.LogError("payments query", ex);
        }

        try
        {
            var deviceLimit = subscription.Subscription?.DevicesLimit ?? 1;
            var registration = await _deviceService.RegisterCurrentDevice(user.Id, deviceLimit);
            devices = await _deviceService.GetUserDevices(user.Id);

            if (!registration.Success)
            {
                return new AccountSnapshot(
                    user,
                    profile,
                    new SubscriptionStatus(false, registration.Error ?? "Устройство не активировано", subscription.Subscription, subscription.EndsAt),
                    payments,
                    devices,
                    registration.Error);
            }
        }
        catch (Exception ex)
        {
            AccountDiagnostics.LogError("devices query", ex);
        }

        return new AccountSnapshot(user, profile, subscription, payments, devices, criticalError);
    }

    private void LogAuthDiagnostics(AccountUser user)
    {
        AccountDiagnostics.Log("Supabase URL: " + _clientFactory.SupabaseUrl);
        AccountDiagnostics.Log("currentUser.email: " + (user.Email ?? "null"));
        AccountDiagnostics.Log("currentUser.id: " + user.Id);
        AccountDiagnostics.Log("hasAccessToken: " + _authService.HasAccessToken);
    }

    private static AccountSnapshot Empty(string? error) =>
        new(null, null, SubscriptionStatus.Missing, Array.Empty<AccountPayment>(), Array.Empty<AccountDevice>(), error);

    private static string FriendlyError(Exception ex) =>
        ex is TimeoutException
            ? ex.Message
            : ex.Message.Contains("network", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("socket", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("internet", StringComparison.OrdinalIgnoreCase)
            ? "Нет подключения к серверу DoubleMark. Проверьте интернет и попробуйте снова."
            : "Не удалось загрузить данные аккаунта DoubleMark. Попробуйте обновить данные позже.";
}
