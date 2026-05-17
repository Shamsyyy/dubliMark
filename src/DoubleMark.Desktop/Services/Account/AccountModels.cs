namespace DoubleMark.Desktop.Services.Account;

public sealed record AccountUser(string Id, string? Email);

public sealed record AccountProfile(
    string UserId,
    string? Email,
    string? Organization,
    string? Inn,
    string? Phone,
    string? Role);

public sealed record ProfileUpdate(string? Organization, string? Inn, string? Phone);

public sealed record AccountSubscription(
    string? Id,
    string UserId,
    string? PlanId,
    string? Status,
    DateTimeOffset? CurrentPeriodStart,
    DateTimeOffset? CurrentPeriodEnd,
    DateTimeOffset? TrialEndsAt,
    int DevicesLimit,
    string? ProviderSubscriptionId = null);

public sealed record SubscriptionStatus(
    bool IsActive,
    string DisplayStatus,
    AccountSubscription? Subscription,
    DateTimeOffset? EndsAt)
{
    public static SubscriptionStatus Missing { get; } =
        new(false, "Подписка не активна", null, null);
}

public sealed record AccountPayment(
    string? Id,
    DateTimeOffset? CreatedAt,
    string? Plan,
    decimal? Amount,
    string? Currency,
    string? Status);

public sealed record AccountDevice(
    string UserId,
    string DeviceId,
    string DeviceName,
    string Platform,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? LastSeenAt);

public sealed record DeviceRegistrationResult(bool Success, string? Error, AccountDevice? Device);

public sealed record AccountSnapshot(
    AccountUser? User,
    AccountProfile? Profile,
    SubscriptionStatus Subscription,
    IReadOnlyList<AccountPayment> Payments,
    IReadOnlyList<AccountDevice> Devices,
    string? Error = null);
