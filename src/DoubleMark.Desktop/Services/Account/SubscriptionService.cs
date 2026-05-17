namespace DoubleMark.Desktop.Services.Account;

public sealed class SubscriptionService
{
    private readonly SupabaseClientFactory _clientFactory;

    public SubscriptionService(SupabaseClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }

    public async Task<AccountSubscription?> GetCurrentSubscription(string userId)
    {
        var result = await _clientFactory.GetClient()
            .From<SubscriptionRow>()
            .Where(row => row.UserId == userId)
            .Get();

        AccountDiagnostics.Log("subscription query result: count=" + result.Models.Count);
        foreach (var row in result.Models)
        {
            AccountDiagnostics.Log(
                $"subscription row: user_id={row.UserId}, plan_id={row.PlanId}, status={row.Status}, current_period_end={row.CurrentPeriodEnd:O}, trial_ends_at={row.TrialEndsAt:O}, devices_limit={row.DevicesLimit}");
        }

        return result.Models
            .Select(AccountRowMapping.ToSubscription)
            .OrderByDescending(subscription => subscription.CurrentPeriodEnd ?? subscription.TrialEndsAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
    }

    public async Task<bool> HasActiveSubscription(string userId) =>
        (await GetSubscriptionStatus(userId)).IsActive;

    public async Task<SubscriptionStatus> GetSubscriptionStatus(string userId) =>
        SubscriptionRules.GetStatus(await GetCurrentSubscription(userId));

    public async Task<IReadOnlyList<AccountPayment>> GetUserPayments(string userId)
    {
        var result = await _clientFactory.GetClient()
            .From<PaymentRow>()
            .Where(row => row.UserId == userId)
            .Get();

        AccountDiagnostics.Log("payments query count: " + result.Models.Count);
        return result.Models
            .Select(AccountRowMapping.ToPayment)
            .OrderByDescending(payment => payment.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }
}
