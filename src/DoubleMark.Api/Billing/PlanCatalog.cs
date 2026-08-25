namespace DoubleMark.Api.Billing;

public enum BillingPeriod
{
    Monthly,
    Yearly
}

public enum PlanTier
{
    Base,
    Standard,
    Elite
}

public sealed record PlanDefinition(
    PlanTier Tier,
    string Name,
    int MonthlyPriceRub,
    int YearlyPriceRub,
    int YearlyMonthlyPriceRub,
    int DevicesLimit,
    int TrialDays,
    bool BetaFeatures,
    IReadOnlyList<string> Features);

public sealed record ResolvedPlan(
    string PlanId,
    PlanTier Tier,
    BillingPeriod Period,
    string Name,
    int PriceRub,
    int DisplayPriceRub,
    int DevicesLimit,
    int TrialDays,
    int SubscriptionDays,
    bool BetaFeatures,
    IReadOnlyList<string> Features);

public static class PlanCatalog
{
    public const int YearlyTrialDays = 30;

    private static readonly IReadOnlyList<PlanDefinition> Plans =
    [
        new(
            PlanTier.Base,
            "Base",
            MonthlyPriceRub: 3000,
            YearlyPriceRub: 28800,
            YearlyMonthlyPriceRub: 2400,
            DevicesLimit: 1,
            TrialDays: 14,
            BetaFeatures: false,
            Features:
            [
                "14 дней бесплатного периода",
                "1 устройство",
                "Неограниченное количество кодов",
                "Windows-приложение",
                "Android-приложение",
                "Инструкции по настройке сканера и принтера",
                "Поддержка"
            ]),
        new(
            PlanTier.Standard,
            "Standard",
            MonthlyPriceRub: 5000,
            YearlyPriceRub: 48000,
            YearlyMonthlyPriceRub: 4000,
            DevicesLimit: 3,
            TrialDays: 14,
            BetaFeatures: false,
            Features:
            [
                "14 дней бесплатного периода",
                "До 3 устройств",
                "Неограниченное количество кодов",
                "Windows-приложение",
                "Android-приложение",
                "Инструкции по настройке сканера и принтера",
                "Приоритетная поддержка"
            ]),
        new(
            PlanTier.Elite,
            "Elite",
            MonthlyPriceRub: 10000,
            YearlyPriceRub: 96000,
            YearlyMonthlyPriceRub: 8000,
            DevicesLimit: 10,
            TrialDays: 14,
            BetaFeatures: true,
            Features:
            [
                "14 дней бесплатного периода",
                "До 10 устройств",
                "Windows-приложение",
                "Android-приложение",
                "Beta-функции",
                "Инструкции по настройке сканера и принтера",
                "Выделенный менеджер"
            ])
    ];

    public static IReadOnlyList<PlanDefinition> All => Plans;

    public static PlanDefinition? GetByTier(PlanTier tier) =>
        Plans.FirstOrDefault(p => p.Tier == tier);

    public static int GetDevicesLimitForPlanId(string? planId)
    {
        var resolved = Resolve(planId);
        return resolved?.DevicesLimit ?? 1;
    }

    public static string ToPlanId(PlanTier tier, BillingPeriod period) =>
        $"{TierToSlug(tier)}-{PeriodToSlug(period)}";

    public static ResolvedPlan? Resolve(string? planId)
    {
        if (string.IsNullOrWhiteSpace(planId))
            return null;

        var parts = planId.Trim().ToLowerInvariant().Split('-', 2);
        if (parts.Length != 2)
            return null;

        if (!TryParseTier(parts[0], out var tier))
            return null;
        if (!TryParsePeriod(parts[1], out var period))
            return null;

        var plan = GetByTier(tier);
        if (plan == null)
            return null;

        var trialDays = period == BillingPeriod.Yearly ? YearlyTrialDays : plan.TrialDays;
        var priceRub = period == BillingPeriod.Yearly ? plan.YearlyPriceRub : plan.MonthlyPriceRub;
        var displayPriceRub = period == BillingPeriod.Yearly
            ? plan.YearlyMonthlyPriceRub
            : plan.MonthlyPriceRub;

        var features = plan.Features
            .Select(f => f.StartsWith("14 дней", StringComparison.Ordinal) && period == BillingPeriod.Yearly
                ? $"{YearlyTrialDays} дней бесплатного периода"
                : f)
            .ToList();

        return new ResolvedPlan(
            ToPlanId(tier, period),
            tier,
            period,
            plan.Name,
            priceRub,
            displayPriceRub,
            plan.DevicesLimit,
            trialDays,
            period == BillingPeriod.Yearly ? 365 : 30,
            plan.BetaFeatures,
            features);
    }

    public static IReadOnlyList<ResolvedPlan> ListForPeriod(BillingPeriod period) =>
        Plans
            .Select(p => Resolve(ToPlanId(p.Tier, period))!)
            .ToList();

    public static string TierToSlug(PlanTier tier) => tier switch
    {
        PlanTier.Base => "base",
        PlanTier.Standard => "standard",
        PlanTier.Elite => "elite",
        _ => "base"
    };

    public static string PeriodToSlug(BillingPeriod period) =>
        period == BillingPeriod.Yearly ? "yearly" : "monthly";

    public static bool TryParseTier(string raw, out PlanTier tier)
    {
        switch (raw)
        {
            case "base":
                tier = PlanTier.Base;
                return true;
            case "standard":
                tier = PlanTier.Standard;
                return true;
            case "elite":
                tier = PlanTier.Elite;
                return true;
            default:
                tier = PlanTier.Base;
                return false;
        }
    }

    public static bool TryParsePeriod(string raw, out BillingPeriod period)
    {
        switch (raw)
        {
            case "monthly":
                period = BillingPeriod.Monthly;
                return true;
            case "yearly":
                period = BillingPeriod.Yearly;
                return true;
            default:
                period = BillingPeriod.Monthly;
                return false;
        }
    }
}
