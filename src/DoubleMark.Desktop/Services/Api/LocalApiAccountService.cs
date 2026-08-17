using System.IO;
using System.Security.Cryptography;
using System.Text;
using DoubleMark.Desktop.Services.Account;

namespace DoubleMark.Desktop.Services.Api;

public static class LocalDeviceIdentity
{
    private static readonly object Gate = new();

    public static string GetDeviceId()
    {
        lock (Gate)
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DoubleMark",
                "device.id");
            if (File.Exists(path))
                return File.ReadAllText(path).Trim();

            var raw = string.Join("|", Environment.MachineName, Environment.UserName, Environment.OSVersion.VersionString);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, hash);
            File.Move(tmp, path, overwrite: false);
            return hash;
        }
    }

    public static string GetDeviceName() => Environment.MachineName;
    public static string GetPlatform() => "Windows";
}

public sealed class LocalApiAccountService : IAccountPortal
{
    private readonly AuthService _authService;
    private readonly LocalApiProfileService _profileService;
    private readonly LocalApiSubscriptionService _subscriptionService;
    private readonly LocalApiPaymentService _paymentService;
    private readonly LocalApiDeviceService _deviceService;
    private readonly string _apiBaseUrl;

    public LocalApiAccountService(
        AuthService authService,
        LocalApiProfileService profileService,
        LocalApiSubscriptionService subscriptionService,
        LocalApiPaymentService paymentService,
        LocalApiDeviceService deviceService,
        string apiBaseUrl)
    {
        _authService = authService;
        _profileService = profileService;
        _subscriptionService = subscriptionService;
        _paymentService = paymentService;
        _deviceService = deviceService;
        _apiBaseUrl = apiBaseUrl;
    }

    public bool IsConfigured => _authService.IsConfigured;

    public async Task<AccountSnapshot> RestoreAccount()
    {
        if (!IsConfigured)
            return Empty("Не настроено подключение к API. Проверьте Backend:ApiBaseUrl (localhost:5080).");

        var user = await _authService.RestoreSession();
        return user == null ? Empty(null) : await LoadAccount(user);
    }

    public async Task<AccountSnapshot> SignIn(string email, string password)
    {
        var user = await _authService.SignIn(email, password);
        if (user == null)
            return Empty("Не удалось войти в DoubleMark.");
        return await LoadAccount(user);
    }

    public Task SignOut() => _authService.SignOut();

    public async Task<AccountSnapshot> Refresh()
    {
        var user = _authService.GetCurrentUser();
        return user == null ? Empty(null) : await LoadAccount(user);
    }

    private async Task<AccountSnapshot> LoadAccount(AccountUser user)
    {
        AccountDiagnostics.Log("API URL: " + _apiBaseUrl);
        AccountDiagnostics.Log("currentUser.email: " + (user.Email ?? "null"));
        AccountDiagnostics.Log("currentUser.id: " + user.Id);
        AccountDiagnostics.Log("hasAccessToken: " + _authService.HasAccessToken);

        AccountProfile? profile = null;
        SubscriptionStatus subscription = SubscriptionStatus.Missing;
        IReadOnlyList<AccountPayment> payments = Array.Empty<AccountPayment>();
        IReadOnlyList<AccountDevice> devices = Array.Empty<AccountDevice>();
        string? criticalError = null;

        try { profile = await _profileService.GetOrCreateProfile(user); }
        catch (Exception ex)
        {
            AccountDiagnostics.LogError("profile query", ex);
            criticalError = FriendlyError(ex);
        }

        try { subscription = await _subscriptionService.GetSubscriptionStatus(user.Id); }
        catch (Exception ex)
        {
            AccountDiagnostics.LogError("subscription query", ex);
            criticalError ??= FriendlyError(ex);
        }

        try { payments = await _paymentService.GetUserPayments(user.Id); }
        catch (Exception ex) { AccountDiagnostics.LogError("payments query", ex); }

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
        catch (Exception ex) { AccountDiagnostics.LogError("devices query", ex); }

        return new AccountSnapshot(user, profile, subscription, payments, devices, criticalError);
    }

    private static AccountSnapshot Empty(string? error) =>
        new(null, null, SubscriptionStatus.Missing, Array.Empty<AccountPayment>(), Array.Empty<AccountDevice>(), error);

    private static string FriendlyError(Exception ex) =>
        ex.Message.Contains("refused", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("actively refused", StringComparison.OrdinalIgnoreCase)
            ? "API DoubleMark не запущен. Выполните: docker compose up -d и dotnet run --project src/DoubleMark.Api"
            : "Не удалось загрузить данные аккаунта DoubleMark.";
}
