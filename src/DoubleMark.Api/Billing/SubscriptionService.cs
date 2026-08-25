using DoubleMark.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DoubleMark.Api.Billing;

public sealed class SubscriptionService
{
    private readonly AppDbContext _db;

    public SubscriptionService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Guid?> GetOrgIdForUserAsync(Guid userId, CancellationToken ct)
    {
        var profile = await _db.Profiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == userId, ct);
        return profile?.OrgId;
    }

    public async Task<string?> GetOrgRoleForUserAsync(Guid userId, CancellationToken ct)
    {
        var profile = await _db.Profiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == userId, ct);
        return profile?.OrgRole;
    }

    public async Task<SubscriptionEntity?> GetOrgSubscriptionAsync(Guid orgId, CancellationToken ct) =>
        await _db.Subscriptions
            .Where(s => s.OrgId == orgId)
            .OrderByDescending(s => s.UpdatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<SubscriptionEntity?> GetSubscriptionForUserAsync(Guid userId, CancellationToken ct)
    {
        var orgId = await GetOrgIdForUserAsync(userId, ct);
        if (orgId == null)
        {
            return await _db.Subscriptions.FirstOrDefaultAsync(s => s.UserId == userId, ct);
        }

        return await GetOrgSubscriptionAsync(orgId.Value, ct);
    }

    public static bool IsSubscriptionActive(SubscriptionEntity? subscription)
    {
        if (subscription == null)
            return false;

        var now = DateTime.UtcNow;
        if (subscription.Status is "active" or "past_due"
            && subscription.CurrentPeriodEnd is { } end
            && end > now)
        {
            return true;
        }

        return subscription.Status == "trialing"
               && subscription.TrialEndsAt is { } trialEnd
               && trialEnd > now;
    }

    public async Task SyncEntitlementsAsync(Guid orgId, SubscriptionEntity subscription, CancellationToken ct)
    {
        var entitlement = await _db.Entitlements.FirstOrDefaultAsync(e => e.OrgId == orgId, ct);
        var canDownload = IsSubscriptionActive(subscription);
        if (entitlement == null)
        {
            _db.Entitlements.Add(new EntitlementEntity
            {
                OrgId = orgId,
                CanDownload = canDownload,
                DevicesLimit = subscription.DevicesLimit,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            entitlement.CanDownload = canDownload;
            entitlement.DevicesLimit = subscription.DevicesLimit;
            entitlement.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<SubscriptionEntity> EnsureTrialForNewOrgAsync(
        Guid orgId,
        Guid ownerUserId,
        CancellationToken ct)
    {
        var existing = await GetOrgSubscriptionAsync(orgId, ct);
        if (existing != null)
            return existing;

        var plan = PlanCatalog.GetByTier(PlanTier.Base)!;
        var now = DateTime.UtcNow;
        var subscription = new SubscriptionEntity
        {
            Id = Guid.NewGuid(),
            UserId = ownerUserId,
            OrgId = orgId,
            PlanId = PlanCatalog.ToPlanId(PlanTier.Base, BillingPeriod.Monthly),
            Status = "trialing",
            CurrentPeriodStart = now,
            CurrentPeriodEnd = now.AddDays(plan.TrialDays),
            TrialEndsAt = now.AddDays(plan.TrialDays),
            DevicesLimit = plan.DevicesLimit,
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.Subscriptions.Add(subscription);
        await _db.SaveChangesAsync(ct);
        await SyncEntitlementsAsync(orgId, subscription, ct);
        return subscription;
    }

    public async Task ActivatePlanAsync(
        Guid orgId,
        Guid payingUserId,
        string planId,
        string? providerPaymentId,
        CancellationToken ct)
    {
        var resolved = PlanCatalog.Resolve(planId)
                       ?? throw new ArgumentException("Неизвестный тариф.");

        var now = DateTime.UtcNow;
        var subscription = await GetOrgSubscriptionAsync(orgId, ct);
        if (subscription == null)
        {
            subscription = new SubscriptionEntity
            {
                Id = Guid.NewGuid(),
                UserId = payingUserId,
                OrgId = orgId,
                CreatedAt = now
            };
            _db.Subscriptions.Add(subscription);
        }

        subscription.UserId = payingUserId;
        subscription.OrgId = orgId;
        subscription.PlanId = resolved.PlanId;
        subscription.Status = "active";
        subscription.CurrentPeriodStart = now;
        subscription.CurrentPeriodEnd = now.AddDays(resolved.SubscriptionDays);
        subscription.TrialEndsAt = null;
        subscription.DevicesLimit = resolved.DevicesLimit;
        subscription.ProviderSubscriptionId = providerPaymentId;
        subscription.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);
        await SyncEntitlementsAsync(orgId, subscription, ct);
        await EnforceDeviceLimitAsync(orgId, resolved.DevicesLimit, ct);
    }

    public async Task EnforceDeviceLimitAsync(Guid orgId, int devicesLimit, CancellationToken ct)
    {
        var active = await _db.Devices
            .Where(d => d.OrgId == orgId && d.RevokedAt == null)
            .OrderByDescending(d => d.LastSeenAt)
            .ToListAsync(ct);

        if (active.Count <= devicesLimit)
            return;

        var toRevoke = active.Skip(devicesLimit).ToList();
        var now = DateTime.UtcNow;
        foreach (var device in toRevoke)
            device.RevokedAt = now;

        await _db.SaveChangesAsync(ct);
    }

    public async Task<int> CountActiveDevicesAsync(Guid orgId, CancellationToken ct) =>
        await _db.Devices.CountAsync(d => d.OrgId == orgId && d.RevokedAt == null, ct);

    public async Task BackfillOrgIdsAsync(CancellationToken ct)
    {
        var profiles = await _db.Profiles
            .Where(p => p.OrgId != null)
            .Select(p => new { p.Id, OrgId = p.OrgId!.Value })
            .ToListAsync(ct);
        var byUser = profiles.ToDictionary(p => p.Id, p => p.OrgId);

        var subscriptions = await _db.Subscriptions.Where(s => s.OrgId == null).ToListAsync(ct);
        foreach (var sub in subscriptions)
        {
            if (byUser.TryGetValue(sub.UserId, out var orgId))
                sub.OrgId = orgId;
        }

        var devices = await _db.Devices.Where(d => d.OrgId == null).ToListAsync(ct);
        foreach (var device in devices)
        {
            if (byUser.TryGetValue(device.UserId, out var orgId))
                device.OrgId = orgId;
        }

        var templates = await _db.PrintTemplates.Where(t => t.OrgId == null).ToListAsync(ct);
        foreach (var template in templates)
        {
            if (byUser.TryGetValue(template.UserId, out var orgId))
                template.OrgId = orgId;
        }

        var scans = await _db.ScanHistory.Where(s => s.OrgId == null).ToListAsync(ct);
        foreach (var scan in scans)
        {
            if (byUser.TryGetValue(scan.UserId, out var orgId))
                scan.OrgId = orgId;
        }

        var payments = await _db.Payments.Where(p => p.OrgId == null).ToListAsync(ct);
        foreach (var payment in payments)
        {
            if (byUser.TryGetValue(payment.UserId, out var orgId))
                payment.OrgId = orgId;
        }

        await _db.SaveChangesAsync(ct);
    }
}
