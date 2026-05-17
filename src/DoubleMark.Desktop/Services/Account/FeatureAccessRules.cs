namespace DoubleMark.Desktop.Services.Account;

public static class FeatureAccessRules
{
    public static bool CanUsePremiumFeature(SubscriptionStatus subscriptionStatus) =>
        subscriptionStatus.IsActive;
}
