using DoubleMark.Api.Data;
using DoubleMark.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DoubleMark.Api.Billing;

public sealed record CheckoutRequest(string PlanId);

public sealed record CheckoutResponse(
    Guid PaymentId,
    string PlanId,
    decimal Amount,
    string Currency,
    string Status,
    string PaymentUrl,
    bool Sandbox);

public sealed record PaymentStatusDto(
    Guid Id,
    string? PlanId,
    decimal? Amount,
    string? Currency,
    string? Status,
    string? ProviderPaymentId,
    DateTime CreatedAt);

public sealed record AlphaWebhookRequest(
    string? OrderNumber,
    string? OrderId,
    string? Status,
    string? Operation,
    string? Checksum);

public sealed class BillingService
{
    private readonly AppDbContext _db;
    private readonly SubscriptionService _subscriptions;
    private readonly AlphaBankClient _alpha;
    private readonly AlphaBankOptions _options;

    public BillingService(
        AppDbContext db,
        SubscriptionService subscriptions,
        AlphaBankClient alpha,
        IOptions<AlphaBankOptions> options)
    {
        _db = db;
        _subscriptions = subscriptions;
        _alpha = alpha;
        _options = options.Value;
    }

    public IReadOnlyList<object> ListPlans(string period)
    {
        var billingPeriod = string.Equals(period, "yearly", StringComparison.OrdinalIgnoreCase)
            ? BillingPeriod.Yearly
            : BillingPeriod.Monthly;

        return PlanCatalog.ListForPeriod(billingPeriod)
            .Select(p => new
            {
                planId = p.PlanId,
                tier = PlanCatalog.TierToSlug(p.Tier),
                period = PlanCatalog.PeriodToSlug(p.Period),
                name = p.Name,
                priceRub = p.PriceRub,
                displayPriceRub = p.DisplayPriceRub,
                devicesLimit = p.DevicesLimit,
                trialDays = p.TrialDays,
                subscriptionDays = p.SubscriptionDays,
                betaFeatures = p.BetaFeatures,
                features = p.Features
            })
            .Cast<object>()
            .ToList();
    }

    public async Task<CheckoutResponse> CreateCheckoutAsync(Guid userId, string planId, CancellationToken ct)
    {
        var resolved = PlanCatalog.Resolve(planId)
                       ?? throw new ArgumentException("Неизвестный тариф.");

        var profile = await _db.Profiles.FirstOrDefaultAsync(p => p.Id == userId, ct)
                      ?? throw new UnauthorizedAccessException("Профиль не найден.");
        if (profile.OrgId == null)
            throw new InvalidOperationException("Организация не привязана к аккаунту.");
        if (!string.Equals(profile.OrgRole, "owner", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Оплату может оформить только владелец организации.");

        var orgId = profile.OrgId.Value;
        var now = DateTime.UtcNow;
        var payment = new PaymentEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            OrgId = orgId,
            CreatedAt = now,
            PlanId = resolved.PlanId,
            Amount = resolved.PriceRub,
            Currency = "RUB",
            Status = "pending",
            Provider = "alpha-bank",
            ProviderPaymentId = null
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync(ct);

        var returnUrl = $"{_options.ReturnUrl.TrimEnd('/')}?paymentId={payment.Id:D}";
        var amountKopecks = resolved.PriceRub * 100;
        var register = await _alpha.RegisterOrderAsync(
            payment.Id,
            amountKopecks,
            $"DoubleMark {resolved.Name} ({resolved.PlanId})",
            returnUrl,
            ct);

        if (!register.Success)
        {
            payment.Status = "failed";
            await _db.SaveChangesAsync(ct);
            throw new InvalidOperationException(register.Error ?? "Не удалось создать платёж.");
        }

        payment.ProviderPaymentId = register.OrderId;
        await _db.SaveChangesAsync(ct);

        if (!_alpha.IsLive)
        {
            var sandboxUrl =
                $"/api/billing/sandbox/confirm?paymentId={payment.Id:D}";
            return new CheckoutResponse(
                payment.Id,
                resolved.PlanId,
                resolved.PriceRub,
                "RUB",
                payment.Status ?? "pending",
                sandboxUrl,
                Sandbox: true);
        }

        return new CheckoutResponse(
            payment.Id,
            resolved.PlanId,
            resolved.PriceRub,
            "RUB",
            payment.Status ?? "pending",
            register.FormUrl!,
            Sandbox: false);
    }

    public async Task<PaymentStatusDto?> GetPaymentAsync(Guid userId, Guid paymentId, CancellationToken ct)
    {
        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId, ct);
        if (payment == null)
            return null;

        var profile = await _db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == userId, ct);
        if (payment.UserId != userId && (profile?.OrgId == null || payment.OrgId != profile.OrgId))
            throw new UnauthorizedAccessException("Нет доступа к платежу.");

        return ToDto(payment);
    }

    public async Task<PaymentStatusDto> ConfirmSandboxAsync(Guid userId, Guid paymentId, CancellationToken ct)
    {
        if (_alpha.IsLive)
            throw new InvalidOperationException("Sandbox confirm недоступен в production.");

        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId && p.UserId == userId, ct)
                      ?? throw new KeyNotFoundException("Платёж не найден.");

        await ActivatePaymentAsync(payment, payment.ProviderPaymentId ?? payment.Id.ToString("N"), ct);
        return ToDto(payment);
    }

    public async Task HandleWebhookAsync(AlphaWebhookRequest request, string rawBody, string? signature, CancellationToken ct)
    {
        if (!_alpha.VerifyWebhookSignature(signature ?? request.Checksum, rawBody))
            throw new UnauthorizedAccessException("Неверная подпись webhook.");

        var status = (request.Status ?? request.Operation ?? "").Trim().ToLowerInvariant();
        var succeeded = status is "1" or "2" or "deposited" or "approved" or "success" or "succeeded";
        if (!succeeded)
            return;

        PaymentEntity? payment = null;
        if (Guid.TryParse(request.OrderNumber, out var paymentId))
            payment = await _db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId, ct);

        if (payment == null && !string.IsNullOrWhiteSpace(request.OrderId))
            payment = await _db.Payments.FirstOrDefaultAsync(p => p.ProviderPaymentId == request.OrderId, ct);

        if (payment == null)
            throw new KeyNotFoundException("Платёж не найден.");

        if (string.Equals(payment.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
            return;

        await ActivatePaymentAsync(payment, request.OrderId ?? payment.ProviderPaymentId, ct);
    }

    public async Task ActivateByAdminAsync(Guid orgId, Guid adminUserId, string planId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var resolved = PlanCatalog.Resolve(planId)
                       ?? throw new ArgumentException("Неизвестный тариф.");

        _db.Payments.Add(new PaymentEntity
        {
            Id = Guid.NewGuid(),
            UserId = adminUserId,
            OrgId = orgId,
            CreatedAt = now,
            PlanId = resolved.PlanId,
            Amount = 0,
            Currency = "RUB",
            Status = "succeeded",
            Provider = "admin",
            ProviderPaymentId = $"admin-{Guid.NewGuid():N}"
        });
        await _db.SaveChangesAsync(ct);
        await _subscriptions.ActivatePlanAsync(orgId, adminUserId, resolved.PlanId, null, ct);
    }

    private async Task ActivatePaymentAsync(PaymentEntity payment, string? providerPaymentId, CancellationToken ct)
    {
        if (payment.OrgId == null)
            throw new InvalidOperationException("У платежа нет org_id.");
        if (string.IsNullOrWhiteSpace(payment.PlanId))
            throw new InvalidOperationException("У платежа нет plan_id.");

        if (!string.IsNullOrWhiteSpace(providerPaymentId))
        {
            var duplicate = await _db.Payments.AnyAsync(
                p => p.Id != payment.Id
                     && p.ProviderPaymentId == providerPaymentId
                     && p.Status == "succeeded",
                ct);
            if (duplicate)
                return;
        }

        payment.Status = "succeeded";
        payment.ProviderPaymentId ??= providerPaymentId;
        await _db.SaveChangesAsync(ct);
        await _subscriptions.ActivatePlanAsync(
            payment.OrgId.Value,
            payment.UserId,
            payment.PlanId,
            payment.ProviderPaymentId,
            ct);
    }

    private static PaymentStatusDto ToDto(PaymentEntity payment) =>
        new(
            payment.Id,
            payment.PlanId,
            payment.Amount,
            payment.Currency,
            payment.Status,
            payment.ProviderPaymentId,
            payment.CreatedAt);
}
