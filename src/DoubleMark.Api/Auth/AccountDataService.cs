using System.Security.Claims;
using DoubleMark.Api.Billing;
using DoubleMark.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DoubleMark.Api.Auth;

public sealed record ProfileDto(
    Guid UserId,
    string? Email,
    string? Organization,
    string? Inn,
    string? Phone,
    string? Role,
    Guid? OrgId = null,
    string? OrgRole = null);

public sealed record ProfileUpdateRequest(string? Organization, string? Inn, string? Phone);

public sealed record SubscriptionDto(
    Guid Id,
    Guid UserId,
    Guid? OrgId,
    string? PlanId,
    string Status,
    DateTime? CurrentPeriodStart,
    DateTime? CurrentPeriodEnd,
    DateTime? TrialEndsAt,
    int DevicesLimit,
    string? ProviderSubscriptionId,
    int ActiveDeviceCount = 0);

public sealed record PaymentDto(
    Guid Id,
    DateTime CreatedAt,
    string? PlanId,
    decimal? Amount,
    string? Currency,
    string? Status);

public sealed record DeviceDto(
    Guid UserId,
    Guid? OrgId,
    string DeviceId,
    string DeviceName,
    string Platform,
    DateTime CreatedAt,
    DateTime LastSeenAt,
    DateTime? RevokedAt = null);

public sealed record DeviceUpsertRequest(
    string DeviceId,
    string DeviceName,
    string Platform);

public sealed record DeviceRegistrationResponse(bool Success, string? Error, DeviceDto? Device);

public sealed record TemplateDto(
    Guid Id,
    Guid UserId,
    Guid? OrgId,
    string Name,
    string? Description,
    decimal WidthMm,
    decimal HeightMm,
    string? PrinterName,
    string TemplateData,
    bool IsDefault,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record TemplateUpsertRequest(
    Guid? Id,
    string Name,
    string? Description,
    decimal WidthMm,
    decimal HeightMm,
    string? PrinterName,
    string TemplateData,
    bool IsDefault);

public sealed record ScanHistoryDto(
    Guid Id,
    Guid UserId,
    Guid? OrgId,
    string RawCode,
    string CodeHash,
    string? Source,
    int? GsCount,
    bool HasAi01,
    bool HasAi21,
    bool HasAi91,
    bool HasAi92,
    string? Gtin,
    string? Serial,
    DateTime ScannedAt,
    DateTime CreatedAt);

public sealed record ScanHistoryCreateRequest(
    string RawCode,
    string CodeHash,
    string? Source,
    int? GsCount,
    bool HasAi01,
    bool HasAi21,
    bool HasAi91,
    bool HasAi92,
    string? Gtin,
    string? Serial,
    DateTime? ScannedAt);

public sealed record InstallerDownloadRequest(string? Version, string? FileName);

public sealed class AccountDataService
{
    public const int MaxScanHistoryPerOrg = 1000;

    private readonly AppDbContext _db;
    private readonly SubscriptionService _subscriptions;

    public AccountDataService(AppDbContext db, SubscriptionService subscriptions)
    {
        _db = db;
        _subscriptions = subscriptions;
    }

    public async Task<ProfileDto> GetOrCreateProfileAsync(Guid userId, string? email, CancellationToken ct)
    {
        var profile = await _db.Profiles.FirstOrDefaultAsync(p => p.Id == userId, ct);
        if (profile == null)
        {
            profile = new ProfileEntity
            {
                Id = userId,
                Email = email,
                Role = "user",
                UpdatedAt = DateTime.UtcNow
            };
            _db.Profiles.Add(profile);
            await _db.SaveChangesAsync(ct);
        }

        return ToProfile(profile);
    }

    public async Task<ProfileDto> UpdateProfileAsync(Guid userId, ProfileUpdateRequest request, CancellationToken ct)
    {
        var profile = await _db.Profiles.FirstOrDefaultAsync(p => p.Id == userId, ct)
                      ?? throw new KeyNotFoundException("Профиль не найден.");

        profile.CompanyName = request.Organization;
        profile.Inn = request.Inn;
        profile.Phone = request.Phone;
        profile.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ToProfile(profile);
    }

    public async Task<SubscriptionDto?> GetSubscriptionAsync(Guid userId, CancellationToken ct)
    {
        var row = await _subscriptions.GetSubscriptionForUserAsync(userId, ct);
        if (row == null)
            return null;

        var activeCount = row.OrgId is { } orgId
            ? await _subscriptions.CountActiveDevicesAsync(orgId, ct)
            : await _db.Devices.CountAsync(d => d.UserId == userId && d.RevokedAt == null, ct);

        return ToSubscription(row, activeCount);
    }

    public async Task<IReadOnlyList<PaymentDto>> GetPaymentsAsync(Guid userId, CancellationToken ct)
    {
        var orgId = await _subscriptions.GetOrgIdForUserAsync(userId, ct);
        var query = orgId == null
            ? _db.Payments.Where(p => p.UserId == userId)
            : _db.Payments.Where(p => p.OrgId == orgId || p.UserId == userId);

        var rows = await query
            .OrderByDescending(p => p.CreatedAt)
            .Take(50)
            .ToListAsync(ct);
        return rows.Select(ToPayment).ToList();
    }

    public async Task<IReadOnlyList<DeviceDto>> GetDevicesAsync(Guid userId, CancellationToken ct)
    {
        var orgId = await _subscriptions.GetOrgIdForUserAsync(userId, ct);
        var query = orgId == null
            ? _db.Devices.Where(d => d.UserId == userId && d.RevokedAt == null)
            : _db.Devices.Where(d => d.OrgId == orgId && d.RevokedAt == null);

        var rows = await query
            .OrderByDescending(d => d.LastSeenAt)
            .ToListAsync(ct);
        return rows.Select(ToDevice).ToList();
    }

    public async Task<DeviceRegistrationResponse> UpsertDeviceAsync(
        Guid userId,
        DeviceUpsertRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceId))
            return new DeviceRegistrationResponse(false, "DeviceId обязателен.", null);

        var profile = await _db.Profiles.FirstOrDefaultAsync(p => p.Id == userId, ct);
        var orgId = profile?.OrgId;
        var subscription = await _subscriptions.GetSubscriptionForUserAsync(userId, ct);
        if (!SubscriptionService.IsSubscriptionActive(subscription))
            return new DeviceRegistrationResponse(false, "Нет активной подписки для регистрации устройства.", null);

        var limit = subscription!.DevicesLimit;
        if (limit <= 0)
            limit = PlanCatalog.GetDevicesLimitForPlanId(subscription.PlanId);

        List<DeviceEntity> devices;
        if (orgId != null)
        {
            devices = await _db.Devices
                .Where(d => d.OrgId == orgId && d.RevokedAt == null)
                .ToListAsync(ct);
        }
        else
        {
            devices = await _db.Devices
                .Where(d => d.UserId == userId && d.RevokedAt == null)
                .ToListAsync(ct);
        }

        var existing = devices.FirstOrDefault(d => d.DeviceId == request.DeviceId)
                       ?? await _db.Devices.FirstOrDefaultAsync(
                           d => d.UserId == userId && d.DeviceId == request.DeviceId,
                           ct);

        if (existing == null && devices.Count >= limit)
        {
            return new DeviceRegistrationResponse(
                false,
                "Превышен лимит устройств по текущему тарифу.",
                null);
        }

        var now = DateTime.UtcNow;
        if (existing == null)
        {
            existing = new DeviceEntity
            {
                UserId = userId,
                OrgId = orgId,
                DeviceId = request.DeviceId.Trim(),
                DeviceName = request.DeviceName?.Trim() ?? "",
                Platform = string.IsNullOrWhiteSpace(request.Platform) ? "Windows" : request.Platform.Trim(),
                CreatedAt = now,
                LastSeenAt = now,
                RevokedAt = null
            };
            _db.Devices.Add(existing);
        }
        else
        {
            if (existing.RevokedAt != null)
            {
                var activeCount = devices.Count(d => d.DeviceId != existing.DeviceId);
                if (activeCount >= limit)
                {
                    return new DeviceRegistrationResponse(
                        false,
                        "Превышен лимит устройств по текущему тарифу.",
                        null);
                }
            }

            existing.DeviceName = request.DeviceName?.Trim() ?? existing.DeviceName;
            existing.Platform = string.IsNullOrWhiteSpace(request.Platform)
                ? existing.Platform
                : request.Platform.Trim();
            existing.LastSeenAt = now;
            existing.OrgId ??= orgId;
            existing.RevokedAt = null;
        }

        await _db.SaveChangesAsync(ct);
        return new DeviceRegistrationResponse(true, null, ToDevice(existing));
    }

    public async Task<bool> RevokeDeviceAsync(Guid userId, string deviceId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return false;

        var profile = await _db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == userId, ct);
        var orgId = profile?.OrgId;
        var isOwner = string.Equals(profile?.OrgRole, "owner", StringComparison.OrdinalIgnoreCase);

        DeviceEntity? device;
        if (orgId != null && isOwner)
        {
            device = await _db.Devices.FirstOrDefaultAsync(
                d => d.OrgId == orgId && d.DeviceId == deviceId && d.RevokedAt == null,
                ct);
        }
        else
        {
            device = await _db.Devices.FirstOrDefaultAsync(
                d => d.UserId == userId && d.DeviceId == deviceId && d.RevokedAt == null,
                ct);
        }

        if (device == null)
            return false;

        device.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<TemplateDto>> GetTemplatesAsync(Guid userId, CancellationToken ct)
    {
        var orgId = await _subscriptions.GetOrgIdForUserAsync(userId, ct);
        var query = orgId == null
            ? _db.PrintTemplates.Where(t => t.UserId == userId)
            : _db.PrintTemplates.Where(t => t.OrgId == orgId || (t.OrgId == null && t.UserId == userId));

        var rows = await query
            .OrderByDescending(t => t.UpdatedAt)
            .ToListAsync(ct);
        return rows.Select(ToTemplate).ToList();
    }

    public async Task<TemplateDto> UpsertTemplateAsync(Guid userId, TemplateUpsertRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Имя шаблона обязательно.");

        var orgId = await _subscriptions.GetOrgIdForUserAsync(userId, ct);
        var now = DateTime.UtcNow;
        PrintTemplateEntity entity;
        if (request.Id is { } id)
        {
            entity = await FindOrgTemplateAsync(userId, orgId, id, ct)
                     ?? throw new KeyNotFoundException("Шаблон не найден.");
            entity.Name = request.Name.Trim();
            entity.Description = request.Description;
            entity.WidthMm = request.WidthMm;
            entity.HeightMm = request.HeightMm;
            entity.PrinterName = request.PrinterName;
            entity.TemplateData = string.IsNullOrWhiteSpace(request.TemplateData) ? "{}" : request.TemplateData;
            entity.IsDefault = request.IsDefault;
            entity.UpdatedAt = now;
            entity.OrgId ??= orgId;
        }
        else
        {
            entity = new PrintTemplateEntity
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                OrgId = orgId,
                Name = request.Name.Trim(),
                Description = request.Description,
                WidthMm = request.WidthMm,
                HeightMm = request.HeightMm,
                PrinterName = request.PrinterName,
                TemplateData = string.IsNullOrWhiteSpace(request.TemplateData) ? "{}" : request.TemplateData,
                IsDefault = request.IsDefault,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.PrintTemplates.Add(entity);
        }

        if (request.IsDefault)
        {
            var othersQuery = orgId == null
                ? _db.PrintTemplates.Where(t => t.UserId == userId && t.Id != entity.Id && t.IsDefault)
                : _db.PrintTemplates.Where(t => t.OrgId == orgId && t.Id != entity.Id && t.IsDefault);
            var others = await othersQuery.ToListAsync(ct);
            foreach (var other in others)
                other.IsDefault = false;
        }

        await _db.SaveChangesAsync(ct);
        return ToTemplate(entity);
    }

    public async Task DeleteTemplateAsync(Guid userId, Guid templateId, CancellationToken ct)
    {
        var orgId = await _subscriptions.GetOrgIdForUserAsync(userId, ct);
        var entity = await FindOrgTemplateAsync(userId, orgId, templateId, ct);
        if (entity == null)
            return;
        _db.PrintTemplates.Remove(entity);
        await _db.SaveChangesAsync(ct);
    }

    public async Task SetDefaultTemplateAsync(Guid userId, Guid templateId, CancellationToken ct)
    {
        var orgId = await _subscriptions.GetOrgIdForUserAsync(userId, ct);
        var templates = orgId == null
            ? await _db.PrintTemplates.Where(t => t.UserId == userId).ToListAsync(ct)
            : await _db.PrintTemplates.Where(t => t.OrgId == orgId || (t.OrgId == null && t.UserId == userId)).ToListAsync(ct);

        var target = templates.FirstOrDefault(t => t.Id == templateId)
                     ?? throw new KeyNotFoundException("Шаблон не найден.");
        foreach (var t in templates)
            t.IsDefault = t.Id == templateId;
        target.UpdatedAt = DateTime.UtcNow;
        target.OrgId ??= orgId;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ScanHistoryDto>> GetScanHistoryAsync(Guid userId, CancellationToken ct)
    {
        var orgId = await _subscriptions.GetOrgIdForUserAsync(userId, ct);
        var query = orgId == null
            ? _db.ScanHistory.Where(s => s.UserId == userId)
            : _db.ScanHistory.Where(s => s.OrgId == orgId || (s.OrgId == null && s.UserId == userId));

        var rows = await query
            .OrderByDescending(s => s.ScannedAt)
            .Take(MaxScanHistoryPerOrg)
            .ToListAsync(ct);
        return rows.Select(ToScan).ToList();
    }

    public async Task<int> GetScanHistoryCountAsync(Guid userId, CancellationToken ct)
    {
        var orgId = await _subscriptions.GetOrgIdForUserAsync(userId, ct);
        if (orgId == null)
            return await _db.ScanHistory.CountAsync(s => s.UserId == userId, ct);
        return await _db.ScanHistory.CountAsync(
            s => s.OrgId == orgId || (s.OrgId == null && s.UserId == userId),
            ct);
    }

    public async Task<ScanHistoryDto> AddScanHistoryAsync(Guid userId, ScanHistoryCreateRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RawCode) || string.IsNullOrWhiteSpace(request.CodeHash))
            throw new ArgumentException("rawCode и codeHash обязательны.");

        var orgId = await _subscriptions.GetOrgIdForUserAsync(userId, ct);
        var now = DateTime.UtcNow;
        var entity = new ScanHistoryEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            OrgId = orgId,
            RawCode = request.RawCode,
            CodeHash = request.CodeHash.ToLowerInvariant(),
            Source = request.Source,
            GsCount = request.GsCount,
            HasAi01 = request.HasAi01,
            HasAi21 = request.HasAi21,
            HasAi91 = request.HasAi91,
            HasAi92 = request.HasAi92,
            Gtin = request.Gtin,
            Serial = request.Serial,
            ScannedAt = request.ScannedAt?.ToUniversalTime() ?? now,
            CreatedAt = now
        };
        _db.ScanHistory.Add(entity);
        await _db.SaveChangesAsync(ct);

        // Keep only newest MaxScanHistoryPerOrg for the org/user.
        var count = await GetScanHistoryCountAsync(userId, ct);
        if (count > MaxScanHistoryPerOrg)
        {
            var overflow = count - MaxScanHistoryPerOrg;
            IQueryable<ScanHistoryEntity> q = orgId == null
                ? _db.ScanHistory.Where(s => s.UserId == userId)
                : _db.ScanHistory.Where(s => s.OrgId == orgId || (s.OrgId == null && s.UserId == userId));
            var old = await q.OrderBy(s => s.ScannedAt).Take(overflow).ToListAsync(ct);
            _db.ScanHistory.RemoveRange(old);
            await _db.SaveChangesAsync(ct);
        }

        return ToScan(entity);
    }

    public async Task DeleteScanHistoryItemAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var orgId = await _subscriptions.GetOrgIdForUserAsync(userId, ct);
        ScanHistoryEntity? entity;
        if (orgId == null)
        {
            entity = await _db.ScanHistory.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
        }
        else
        {
            entity = await _db.ScanHistory.FirstOrDefaultAsync(
                s => s.Id == id && (s.OrgId == orgId || s.UserId == userId),
                ct);
        }

        if (entity == null)
            return;
        _db.ScanHistory.Remove(entity);
        await _db.SaveChangesAsync(ct);
    }

    public async Task ClearScanHistoryAsync(Guid userId, CancellationToken ct)
    {
        var orgId = await _subscriptions.GetOrgIdForUserAsync(userId, ct);
        var rows = orgId == null
            ? await _db.ScanHistory.Where(s => s.UserId == userId).ToListAsync(ct)
            : await _db.ScanHistory.Where(s => s.OrgId == orgId || (s.OrgId == null && s.UserId == userId)).ToListAsync(ct);
        _db.ScanHistory.RemoveRange(rows);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<InstallerDownloadEntity> RecordInstallerDownloadAsync(
        Guid userId,
        string? version,
        string? fileName,
        CancellationToken ct)
    {
        var row = new InstallerDownloadEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Version = string.IsNullOrWhiteSpace(version) ? null : version.Trim(),
            FileName = string.IsNullOrWhiteSpace(fileName) ? null : fileName.Trim(),
            CreatedAt = DateTime.UtcNow
        };
        _db.InstallerDownloads.Add(row);
        await _db.SaveChangesAsync(ct);
        return row;
    }

    public static Guid GetUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? user.FindFirstValue("sub");
        if (!Guid.TryParse(raw, out var id))
            throw new UnauthorizedAccessException("Некорректный пользователь.");
        return id;
    }

    private async Task<PrintTemplateEntity?> FindOrgTemplateAsync(
        Guid userId,
        Guid? orgId,
        Guid templateId,
        CancellationToken ct)
    {
        if (orgId == null)
            return await _db.PrintTemplates.FirstOrDefaultAsync(t => t.Id == templateId && t.UserId == userId, ct);

        return await _db.PrintTemplates.FirstOrDefaultAsync(
            t => t.Id == templateId && (t.OrgId == orgId || (t.OrgId == null && t.UserId == userId)),
            ct);
    }

    private static ProfileDto ToProfile(ProfileEntity p) =>
        new(p.Id, p.Email, p.CompanyName, p.Inn, p.Phone, p.Role, p.OrgId, p.OrgRole);

    private static SubscriptionDto ToSubscription(SubscriptionEntity s, int activeDeviceCount) =>
        new(s.Id, s.UserId, s.OrgId, s.PlanId, s.Status, s.CurrentPeriodStart, s.CurrentPeriodEnd,
            s.TrialEndsAt, s.DevicesLimit, s.ProviderSubscriptionId, activeDeviceCount);

    private static PaymentDto ToPayment(PaymentEntity p) =>
        new(p.Id, p.CreatedAt, p.PlanId, p.Amount, p.Currency, p.Status);

    private static DeviceDto ToDevice(DeviceEntity d) =>
        new(d.UserId, d.OrgId, d.DeviceId, d.DeviceName, d.Platform, d.CreatedAt, d.LastSeenAt, d.RevokedAt);

    private static TemplateDto ToTemplate(PrintTemplateEntity t) =>
        new(t.Id, t.UserId, t.OrgId, t.Name, t.Description, t.WidthMm, t.HeightMm, t.PrinterName,
            t.TemplateData, t.IsDefault, t.CreatedAt, t.UpdatedAt);

    private static ScanHistoryDto ToScan(ScanHistoryEntity s) =>
        new(s.Id, s.UserId, s.OrgId, s.RawCode, s.CodeHash, s.Source, s.GsCount, s.HasAi01, s.HasAi21,
            s.HasAi91, s.HasAi92, s.Gtin, s.Serial, s.ScannedAt, s.CreatedAt);
}
