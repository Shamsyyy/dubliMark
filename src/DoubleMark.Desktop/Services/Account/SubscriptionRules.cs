namespace DoubleMark.Desktop.Services.Account;

public static class SubscriptionRules
{
    public static SubscriptionStatus GetStatus(AccountSubscription? subscription, DateTimeOffset? now = null)
    {
        if (subscription == null)
            return SubscriptionStatus.Missing;

        var current = now ?? DateTimeOffset.UtcNow;
        var status = subscription.Status?.Trim().ToLowerInvariant();

        if (status == "active" && subscription.CurrentPeriodEnd > current)
        {
            return new SubscriptionStatus(
                true,
                "Активна",
                subscription,
                subscription.CurrentPeriodEnd);
        }

        if (status == "trialing" && subscription.TrialEndsAt > current)
        {
            return new SubscriptionStatus(
                true,
                "Trial",
                subscription,
                subscription.TrialEndsAt);
        }

        var endedAt = subscription.CurrentPeriodEnd ?? subscription.TrialEndsAt;
        return new SubscriptionStatus(
            false,
            endedAt.HasValue ? "Подписка истекла" : "Подписка не активна",
            subscription,
            endedAt);
    }
}
