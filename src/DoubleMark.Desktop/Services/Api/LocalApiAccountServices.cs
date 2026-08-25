using DoubleMark.Desktop.Services.Account;

namespace DoubleMark.Desktop.Services.Api;

public sealed class LocalApiProfileService
{
    private readonly DoubleMarkApiClient _api;

    public LocalApiProfileService(DoubleMarkApiClient api) => _api = api;

    public async Task<AccountProfile?> GetOrCreateProfile(AccountUser user)
    {
        var dto = await _api.GetProfileAsync();
        return dto == null
            ? null
            : new AccountProfile(
                dto.UserId.ToString(),
                dto.Email ?? user.Email,
                dto.Organization,
                dto.Inn,
                dto.Phone,
                dto.Role);
    }

    public Task UpdateProfile(string userId, ProfileUpdate update) =>
        _api.UpdateProfileAsync(update)!;
}

public sealed class LocalApiSubscriptionService
{
    private readonly DoubleMarkApiClient _api;

    public LocalApiSubscriptionService(DoubleMarkApiClient api) => _api = api;

    public async Task<SubscriptionStatus> GetSubscriptionStatus(string userId)
    {
        var dto = await _api.GetSubscriptionAsync();
        if (dto == null)
            return SubscriptionStatus.Missing;

        var sub = new AccountSubscription(
            dto.Id.ToString(),
            dto.UserId.ToString(),
            dto.PlanId,
            dto.Status,
            dto.CurrentPeriodStart,
            dto.CurrentPeriodEnd,
            dto.TrialEndsAt,
            dto.DevicesLimit,
            dto.ProviderSubscriptionId);

        return SubscriptionRules.GetStatus(sub);
    }
}

public sealed class LocalApiPaymentService
{
    private readonly DoubleMarkApiClient _api;

    public LocalApiPaymentService(DoubleMarkApiClient api) => _api = api;

    public async Task<IReadOnlyList<AccountPayment>> GetUserPayments(string userId)
    {
        var rows = await _api.GetPaymentsAsync() ?? new List<DoubleMarkApiClient.PaymentDto>();
        return rows.Select(p => new AccountPayment(
            p.Id.ToString(),
            p.CreatedAt,
            p.PlanId,
            p.Amount,
            p.Currency,
            p.Status)).ToList();
    }
}

public sealed class LocalApiDeviceService
{
    private readonly DoubleMarkApiClient _api;

    public LocalApiDeviceService(DoubleMarkApiClient api) => _api = api;

    public async Task<DeviceRegistrationResult> RegisterCurrentDevice(string userId)
    {
        _ = userId;
        var response = await _api.UpsertDeviceAsync(
            LocalDeviceIdentity.GetDeviceId(),
            LocalDeviceIdentity.GetDeviceName(),
            LocalDeviceIdentity.GetPlatform());

        if (response == null)
            return new DeviceRegistrationResult(false, "Пустой ответ сервера.", null);

        var error = response.Error;
        if (!response.Success
            && !string.IsNullOrWhiteSpace(error)
            && error.Contains("лимит", StringComparison.OrdinalIgnoreCase))
        {
            error =
                "Лимит устройств организации исчерпан. Отключите устройство в личном кабинете на сайте DoubleMark.";
        }

        AccountDevice? device = response.Device == null
            ? null
            : new AccountDevice(
                response.Device.UserId.ToString(),
                response.Device.DeviceId,
                response.Device.DeviceName,
                response.Device.Platform,
                response.Device.CreatedAt,
                response.Device.LastSeenAt);

        return new DeviceRegistrationResult(response.Success, error, device);
    }

    public async Task<IReadOnlyList<AccountDevice>> GetUserDevices(string userId)
    {
        var rows = await _api.GetDevicesAsync() ?? new List<DoubleMarkApiClient.DeviceDto>();
        return rows.Select(d => new AccountDevice(
            d.UserId.ToString(),
            d.DeviceId,
            d.DeviceName,
            d.Platform,
            d.CreatedAt,
            d.LastSeenAt)).ToList();
    }
}
