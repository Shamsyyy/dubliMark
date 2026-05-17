namespace DoubleMark.Desktop.Services.Account;

internal static class AccountRowMapping
{
    public static AccountProfile ToProfile(ProfileRow row) =>
        new(row.Id, row.Email, row.CompanyName, row.Inn, row.Phone, row.Role);

    public static AccountSubscription ToSubscription(SubscriptionRow row) =>
        new(
            row.Id,
            row.UserId,
            row.PlanId,
            row.Status,
            ToOffset(row.CurrentPeriodStart),
            ToOffset(row.CurrentPeriodEnd),
            ToOffset(row.TrialEndsAt),
            Math.Max(1, row.DevicesLimit ?? 1),
            row.ProviderSubscriptionId);

    public static AccountPayment ToPayment(PaymentRow row) =>
        new(row.Id, ToOffset(row.CreatedAt), row.PlanId, row.Amount, row.Currency, row.Status);

    public static AccountDevice ToDevice(DeviceRow row) =>
        new(row.UserId, row.DeviceId, row.DeviceName, row.Platform, ToOffset(row.CreatedAt), ToOffset(row.LastSeenAt));

    public static DateTime? ToDateTime(DateTimeOffset value) =>
        value.UtcDateTime;

    private static DateTimeOffset? ToOffset(DateTime? value) =>
        value.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)) : null;
}
