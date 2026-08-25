using DoubleMark.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DoubleMark.Api.Admin;

public sealed record AdminRecentUserDto(
    Guid Id,
    string? Email,
    string? CompanyName,
    string? Role,
    DateTime CreatedAt);

public sealed record AdminRecentPaymentDto(
    Guid Id,
    string? Email,
    string? PlanId,
    decimal Amount,
    string Currency,
    string? Status,
    DateTime CreatedAt);

public sealed record AdminOverviewDto(
    int TotalUsers,
    int NewUsers7d,
    int NewUsers30d,
    int ActiveSubscriptions,
    int TrialingSubscriptions,
    int ExpiredSubscriptions,
    int TotalPayments,
    int SuccessfulPayments,
    decimal RevenueTotal,
    decimal Revenue30d,
    int RegisteredDevices,
    int Organizations,
    int MarkingCodes,
    int CodeOperations,
    IReadOnlyList<AdminRecentUserDto> RecentUsers,
    IReadOnlyList<AdminRecentPaymentDto> RecentPayments);

public sealed record AdminUserDto(
    Guid Id,
    string Email,
    string? CompanyName,
    string? Inn,
    string? Phone,
    string Role,
    string OrgRole,
    Guid? OrgId,
    string? OrgName,
    DateTime CreatedAt,
    string? SubscriptionStatus,
    string? PlanId,
    int DeviceCount,
    int ScanCount,
    bool EmailConfirmed,
    DateTime? EmailConfirmedAt,
    bool HasDownloadedInstaller,
    DateTime? LastInstallerDownloadAt,
    bool HasRegisteredDevice);

public sealed record AdminOrganizationDto(
    Guid Id,
    string LegalName,
    string? Inn,
    string? Phone,
    string? Email,
    DateTime CreatedAt,
    int MemberCount,
    bool CanDownload,
    int DevicesLimit);

public sealed record AdminPaymentRowDto(
    Guid Id,
    Guid UserId,
    string? Email,
    string? PlanId,
    decimal? Amount,
    string? Currency,
    string? Status,
    DateTime CreatedAt);

public sealed record AdminDeviceRowDto(
    Guid UserId,
    string? Email,
    string DeviceId,
    string DeviceName,
    string Platform,
    DateTime CreatedAt,
    DateTime LastSeenAt,
    DateTime? RevokedAt);

public sealed record AdminSetRoleRequest(string Role);

public sealed record AdminActionMessageDto(bool Ok, string Message);

public sealed class AdminService
{
    private readonly AppDbContext _db;
    private readonly Auth.AuthService _auth;

    public AdminService(AppDbContext db, Auth.AuthService auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<bool> IsAdminAsync(Guid userId, CancellationToken ct)
    {
        var role = await _db.Profiles.AsNoTracking()
            .Where(p => p.Id == userId)
            .Select(p => p.Role)
            .FirstOrDefaultAsync(ct);
        return string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<AdminOverviewDto> GetOverviewAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var d7 = now.AddDays(-7);
        var d30 = now.AddDays(-30);

        var totalUsers = await _db.Users.CountAsync(ct);
        var newUsers7d = await _db.Users.CountAsync(u => u.CreatedAt >= d7, ct);
        var newUsers30d = await _db.Users.CountAsync(u => u.CreatedAt >= d30, ct);

        var activeSubscriptions = await _db.Subscriptions.CountAsync(s => s.Status == "active", ct);
        var trialingSubscriptions = await _db.Subscriptions.CountAsync(s => s.Status == "trialing", ct);
        var expiredSubscriptions = await _db.Subscriptions.CountAsync(
            s => s.Status == "expired" || s.Status == "canceled" || s.Status == "cancelled", ct);

        var payments = await _db.Payments.AsNoTracking().ToListAsync(ct);
        var successful = payments.Where(IsSuccessful).ToList();

        var recentUsers = await (
            from u in _db.Users.AsNoTracking()
            join p in _db.Profiles.AsNoTracking() on u.Id equals p.Id into profiles
            from p in profiles.DefaultIfEmpty()
            orderby u.CreatedAt descending
            select new AdminRecentUserDto(
                u.Id,
                p.Email ?? u.Email,
                p.CompanyName,
                p.Role,
                u.CreatedAt)
        ).Take(20).ToListAsync(ct);

        var recentPayments = await (
            from pay in _db.Payments.AsNoTracking()
            join u in _db.Users.AsNoTracking() on pay.UserId equals u.Id
            orderby pay.CreatedAt descending
            select new AdminRecentPaymentDto(
                pay.Id,
                u.Email,
                pay.PlanId ?? "",
                pay.Amount ?? 0,
                pay.Currency ?? "RUB",
                pay.Status,
                pay.CreatedAt)
        ).Take(20).ToListAsync(ct);

        return new AdminOverviewDto(
            totalUsers,
            newUsers7d,
            newUsers30d,
            activeSubscriptions,
            trialingSubscriptions,
            expiredSubscriptions,
            payments.Count,
            successful.Count,
            successful.Sum(p => p.Amount ?? 0),
            successful.Where(p => p.CreatedAt >= d30).Sum(p => p.Amount ?? 0),
            await _db.Devices.CountAsync(ct),
            await _db.Organizations.CountAsync(ct),
            await _db.MarkingCodes.CountAsync(ct),
            await _db.CodeOperations.CountAsync(ct),
            recentUsers,
            recentPayments);
    }

    public async Task<IReadOnlyList<AdminUserDto>> GetUsersAsync(CancellationToken ct)
    {
        var deviceCounts = await _db.Devices.AsNoTracking()
            .Where(d => d.RevokedAt == null)
            .GroupBy(d => d.UserId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var scanCounts = await _db.ScanHistory.AsNoTracking()
            .GroupBy(s => s.UserId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var downloadLatest = await _db.InstallerDownloads.AsNoTracking()
            .GroupBy(d => d.UserId)
            .Select(g => new { g.Key, LastAt = g.Max(x => x.CreatedAt) })
            .ToDictionaryAsync(x => x.Key, x => x.LastAt, ct);

        var rows = await (
            from u in _db.Users.AsNoTracking()
            join p in _db.Profiles.AsNoTracking() on u.Id equals p.Id into profiles
            from p in profiles.DefaultIfEmpty()
            join s in _db.Subscriptions.AsNoTracking() on u.Id equals s.UserId into subs
            from s in subs.DefaultIfEmpty()
            join o in _db.Organizations.AsNoTracking() on p.OrgId equals o.Id into orgs
            from o in orgs.DefaultIfEmpty()
            orderby u.CreatedAt descending
            select new
            {
                u.Id,
                u.Email,
                u.EmailConfirmedAt,
                p,
                s,
                OrgName = o != null ? o.LegalName : null,
                u.CreatedAt
            }
        ).ToListAsync(ct);

        return rows.Select(row =>
        {
            var devices = deviceCounts.GetValueOrDefault(row.Id);
            downloadLatest.TryGetValue(row.Id, out var lastDownload);
            return new AdminUserDto(
                row.Id,
                row.Email,
                row.p?.CompanyName,
                row.p?.Inn,
                row.p?.Phone,
                string.IsNullOrWhiteSpace(row.p?.Role) ? "user" : row.p!.Role!,
                row.p?.OrgRole ?? "owner",
                row.p?.OrgId,
                row.OrgName,
                row.CreatedAt,
                row.s?.Status,
                row.s?.PlanId,
                devices,
                scanCounts.GetValueOrDefault(row.Id),
                row.EmailConfirmedAt != null,
                row.EmailConfirmedAt,
                lastDownload != default,
                lastDownload == default ? null : lastDownload,
                devices > 0);
        }).ToList();
    }

    public async Task<IReadOnlyList<AdminOrganizationDto>> GetOrganizationsAsync(CancellationToken ct)
    {
        var memberCounts = await _db.Profiles.AsNoTracking()
            .Where(p => p.OrgId != null)
            .GroupBy(p => p.OrgId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var orgs = await _db.Organizations.AsNoTracking()
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(ct);
        var entitlements = await _db.Entitlements.AsNoTracking()
            .ToDictionaryAsync(e => e.OrgId, ct);

        return orgs.Select(o =>
        {
            entitlements.TryGetValue(o.Id, out var ent);
            return new AdminOrganizationDto(
                o.Id,
                o.LegalName,
                o.Inn,
                o.Phone,
                o.Email,
                o.CreatedAt,
                memberCounts.GetValueOrDefault(o.Id),
                ent?.CanDownload ?? false,
                ent?.DevicesLimit ?? 0);
        }).ToList();
    }

    public Task<List<AdminPaymentRowDto>> GetPaymentsAsync(CancellationToken ct) =>
        (
            from pay in _db.Payments.AsNoTracking()
            join u in _db.Users.AsNoTracking() on pay.UserId equals u.Id
            orderby pay.CreatedAt descending
            select new AdminPaymentRowDto(
                pay.Id,
                pay.UserId,
                u.Email,
                pay.PlanId,
                pay.Amount,
                pay.Currency,
                pay.Status,
                pay.CreatedAt)
        ).Take(200).ToListAsync(ct);

    public Task<List<AdminDeviceRowDto>> GetDevicesAsync(CancellationToken ct) =>
        (
            from d in _db.Devices.AsNoTracking()
            join u in _db.Users.AsNoTracking() on d.UserId equals u.Id
            orderby d.LastSeenAt descending
            select new AdminDeviceRowDto(
                d.UserId,
                u.Email,
                d.DeviceId,
                d.DeviceName,
                d.Platform,
                d.CreatedAt,
                d.LastSeenAt,
                d.RevokedAt)
        ).Take(200).ToListAsync(ct);

    public async Task<AdminUserDto> SetRoleAsync(Guid userId, string role, CancellationToken ct)
    {
        var normalized = NormalizeRole(role);
        var profile = await _db.Profiles.FirstOrDefaultAsync(p => p.Id == userId, ct)
                      ?? throw new KeyNotFoundException("Пользователь не найден.");

        var current = string.IsNullOrWhiteSpace(profile.Role) ? "user" : profile.Role;
        if (string.Equals(current, "admin", StringComparison.OrdinalIgnoreCase)
            && normalized != "admin")
        {
            var adminCount = await _db.Profiles.CountAsync(p => p.Role == "admin", ct);
            if (adminCount <= 1)
                throw new InvalidOperationException("Нельзя снять роль с последнего администратора.");
        }

        profile.Role = normalized;
        profile.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var users = await GetUsersAsync(ct);
        return users.First(u => u.Id == userId);
    }

    public Task<Auth.AuthActionMessageDto> SendPasswordResetAsync(Guid userId, CancellationToken ct) =>
        _auth.AdminSendPasswordResetAsync(userId, ct);

    public Task<Auth.AuthActionMessageDto> ResendConfirmationAsync(Guid userId, CancellationToken ct) =>
        _auth.AdminResendConfirmationAsync(userId, ct);

    public async Task DeleteUserAsync(Guid userId, Guid actorUserId, CancellationToken ct)
    {
        if (userId == actorUserId)
            throw new InvalidOperationException("Нельзя удалить собственный аккаунт из админ-панели.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new KeyNotFoundException("Пользователь не найден.");

        var profile = await _db.Profiles.FirstOrDefaultAsync(p => p.Id == userId, ct);
        var role = string.IsNullOrWhiteSpace(profile?.Role) ? "user" : profile!.Role!;
        if (string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase))
        {
            var adminCount = await _db.Profiles.CountAsync(p => p.Role == "admin", ct);
            if (adminCount <= 1)
                throw new InvalidOperationException("Нельзя удалить последнего администратора.");
        }

        var orgId = profile?.OrgId;

        _db.InstallerDownloads.RemoveRange(
            await _db.InstallerDownloads.Where(d => d.UserId == userId).ToListAsync(ct));
        _db.AuthActionTokens.RemoveRange(
            await _db.AuthActionTokens.Where(t => t.UserId == userId).ToListAsync(ct));
        _db.RefreshTokens.RemoveRange(
            await _db.RefreshTokens.Where(t => t.UserId == userId).ToListAsync(ct));
        _db.PersonalDataConsents.RemoveRange(
            await _db.PersonalDataConsents.Where(c => c.UserId == userId).ToListAsync(ct));
        _db.ScanHistory.RemoveRange(
            await _db.ScanHistory.Where(s => s.UserId == userId).ToListAsync(ct));
        _db.PrintTemplates.RemoveRange(
            await _db.PrintTemplates.Where(t => t.UserId == userId).ToListAsync(ct));
        _db.Devices.RemoveRange(
            await _db.Devices.Where(d => d.UserId == userId).ToListAsync(ct));
        _db.Payments.RemoveRange(
            await _db.Payments.Where(p => p.UserId == userId).ToListAsync(ct));

        var userSubs = await _db.Subscriptions.Where(s => s.UserId == userId).ToListAsync(ct);
        foreach (var sub in userSubs)
        {
            if (sub.OrgId is { } subOrgId)
            {
                var otherMemberId = await _db.Profiles
                    .Where(p => p.OrgId == subOrgId && p.Id != userId)
                    .Select(p => (Guid?)p.Id)
                    .FirstOrDefaultAsync(ct);
                if (otherMemberId != null)
                {
                    sub.UserId = otherMemberId.Value;
                    sub.UpdatedAt = DateTime.UtcNow;
                    continue;
                }
            }

            _db.Subscriptions.Remove(sub);
        }

        if (profile != null)
            _db.Profiles.Remove(profile);

        _db.Users.Remove(user);
        await _db.SaveChangesAsync(ct);

        if (orgId != null)
        {
            var hasMembers = await _db.Profiles.AnyAsync(p => p.OrgId == orgId, ct);
            if (!hasMembers)
            {
                // Leave org subscription orphan cleanup: remove subscriptions tied only to this empty org.
                _db.Subscriptions.RemoveRange(
                    await _db.Subscriptions.Where(s => s.OrgId == orgId).ToListAsync(ct));
                var entitlement = await _db.Entitlements.FirstOrDefaultAsync(e => e.OrgId == orgId, ct);
                if (entitlement != null)
                    _db.Entitlements.Remove(entitlement);
                var org = await _db.Organizations.FirstOrDefaultAsync(o => o.Id == orgId, ct);
                if (org != null)
                    _db.Organizations.Remove(org);
                await _db.SaveChangesAsync(ct);
            }
        }
    }

    private static string NormalizeRole(string role)
    {
        var value = (role ?? "").Trim().ToLowerInvariant();
        return value switch
        {
            "admin" => "admin",
            "user" => "user",
            _ => throw new ArgumentException("Роль может быть только user или admin.")
        };
    }

    private static bool IsSuccessful(PaymentEntity payment)
    {
        var status = payment.Status?.Trim().ToLowerInvariant();
        return status is "succeeded" or "success" or "paid" or "completed";
    }
}
