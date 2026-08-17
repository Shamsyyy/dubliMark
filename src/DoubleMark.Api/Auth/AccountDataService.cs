using System.Security.Claims;
using DoubleMark.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DoubleMark.Api.Auth;

public sealed record ProfileDto(
    Guid UserId,
    string? Email,
    string? Organization,
    string? Inn,
    string? Phone,
    string? Role);

public sealed record ProfileUpdateRequest(string? Organization, string? Inn, string? Phone);

public sealed record SubscriptionDto(
    Guid Id,
    Guid UserId,
    string? PlanId,
    string Status,
    DateTime? CurrentPeriodStart,
    DateTime? CurrentPeriodEnd,
    DateTime? TrialEndsAt,
    int DevicesLimit,
    string? ProviderSubscriptionId);

public sealed record PaymentDto(
    Guid Id,
    DateTime CreatedAt,
    string? PlanId,
    decimal? Amount,
    string? Currency,
    string? Status);

public sealed record DeviceDto(
    Guid UserId,
    string DeviceId,
    string DeviceName,
    string Platform,
    DateTime CreatedAt,
    DateTime LastSeenAt);

public sealed record DeviceUpsertRequest(
    string DeviceId,
    string DeviceName,
    string Platform,
    int DevicesLimit);

public sealed record DeviceRegistrationResponse(bool Success, string? Error, DeviceDto? Device);

public sealed record TemplateDto(
    Guid Id,
    Guid UserId,
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

public sealed class AccountDataService
{
    private readonly AppDbContext _db;

    public AccountDataService(AppDbContext db)
    {
        _db = db;
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
        var row = await _db.Subscriptions.FirstOrDefaultAsync(s => s.UserId == userId, ct);
        return row == null ? null : ToSubscription(row);
    }

    public async Task<IReadOnlyList<PaymentDto>> GetPaymentsAsync(Guid userId, CancellationToken ct)
    {
        var rows = await _db.Payments
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .Take(50)
            .ToListAsync(ct);
        return rows.Select(ToPayment).ToList();
    }

    public async Task<IReadOnlyList<DeviceDto>> GetDevicesAsync(Guid userId, CancellationToken ct)
    {
        var rows = await _db.Devices
            .Where(d => d.UserId == userId)
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

        var devices = await _db.Devices.Where(d => d.UserId == userId).ToListAsync(ct);
        var existing = devices.FirstOrDefault(d => d.DeviceId == request.DeviceId);
        var limit = Math.Max(1, request.DevicesLimit);
        var knownIds = devices.Select(d => d.DeviceId).ToHashSet(StringComparer.Ordinal);
        if (existing == null && knownIds.Count >= limit)
            return new DeviceRegistrationResponse(false, "Превышен лимит устройств по текущему тарифу.", null);

        var now = DateTime.UtcNow;
        if (existing == null)
        {
            existing = new DeviceEntity
            {
                UserId = userId,
                DeviceId = request.DeviceId.Trim(),
                DeviceName = request.DeviceName?.Trim() ?? "",
                Platform = string.IsNullOrWhiteSpace(request.Platform) ? "Windows" : request.Platform.Trim(),
                CreatedAt = now,
                LastSeenAt = now
            };
            _db.Devices.Add(existing);
        }
        else
        {
            existing.DeviceName = request.DeviceName?.Trim() ?? existing.DeviceName;
            existing.Platform = string.IsNullOrWhiteSpace(request.Platform) ? existing.Platform : request.Platform.Trim();
            existing.LastSeenAt = now;
        }

        await _db.SaveChangesAsync(ct);

        var after = await _db.Devices.Where(d => d.UserId == userId).ToListAsync(ct);
        if (after.Count > limit && after.All(d => d.DeviceId != request.DeviceId) == false)
        {
            // Soft guard: if somehow over limit and this device is newest stranger — already blocked above.
        }

        if (after.Select(d => d.DeviceId).Distinct().Count() > limit &&
            after.Count(d => d.DeviceId != request.DeviceId) >= limit)
        {
            return new DeviceRegistrationResponse(false, "Превышен лимит устройств по текущему тарифу.", null);
        }

        return new DeviceRegistrationResponse(true, null, ToDevice(existing));
    }

    public async Task<IReadOnlyList<TemplateDto>> GetTemplatesAsync(Guid userId, CancellationToken ct)
    {
        var rows = await _db.PrintTemplates
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.UpdatedAt)
            .ToListAsync(ct);
        return rows.Select(ToTemplate).ToList();
    }

    public async Task<TemplateDto> UpsertTemplateAsync(Guid userId, TemplateUpsertRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Имя шаблона обязательно.");

        var now = DateTime.UtcNow;
        PrintTemplateEntity entity;
        if (request.Id is { } id)
        {
            entity = await _db.PrintTemplates.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, ct)
                     ?? throw new KeyNotFoundException("Шаблон не найден.");
            entity.Name = request.Name.Trim();
            entity.Description = request.Description;
            entity.WidthMm = request.WidthMm;
            entity.HeightMm = request.HeightMm;
            entity.PrinterName = request.PrinterName;
            entity.TemplateData = string.IsNullOrWhiteSpace(request.TemplateData) ? "{}" : request.TemplateData;
            entity.IsDefault = request.IsDefault;
            entity.UpdatedAt = now;
        }
        else
        {
            entity = new PrintTemplateEntity
            {
                Id = Guid.NewGuid(),
                UserId = userId,
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
            var others = await _db.PrintTemplates
                .Where(t => t.UserId == userId && t.Id != entity.Id && t.IsDefault)
                .ToListAsync(ct);
            foreach (var other in others)
                other.IsDefault = false;
        }

        await _db.SaveChangesAsync(ct);
        return ToTemplate(entity);
    }

    public async Task DeleteTemplateAsync(Guid userId, Guid templateId, CancellationToken ct)
    {
        var entity = await _db.PrintTemplates.FirstOrDefaultAsync(t => t.Id == templateId && t.UserId == userId, ct);
        if (entity == null)
            return;
        _db.PrintTemplates.Remove(entity);
        await _db.SaveChangesAsync(ct);
    }

    public async Task SetDefaultTemplateAsync(Guid userId, Guid templateId, CancellationToken ct)
    {
        var templates = await _db.PrintTemplates.Where(t => t.UserId == userId).ToListAsync(ct);
        var target = templates.FirstOrDefault(t => t.Id == templateId)
                     ?? throw new KeyNotFoundException("Шаблон не найден.");
        foreach (var t in templates)
            t.IsDefault = t.Id == templateId;
        target.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ScanHistoryDto>> GetScanHistoryAsync(Guid userId, CancellationToken ct)
    {
        var rows = await _db.ScanHistory
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.ScannedAt)
            .Take(1000)
            .ToListAsync(ct);
        return rows.Select(ToScan).ToList();
    }

    public async Task<int> GetScanHistoryCountAsync(Guid userId, CancellationToken ct) =>
        await _db.ScanHistory.CountAsync(s => s.UserId == userId, ct);

    public async Task<ScanHistoryDto> AddScanHistoryAsync(Guid userId, ScanHistoryCreateRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RawCode) || string.IsNullOrWhiteSpace(request.CodeHash))
            throw new ArgumentException("rawCode и codeHash обязательны.");

        var now = DateTime.UtcNow;
        var entity = new ScanHistoryEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
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
        return ToScan(entity);
    }

    public async Task DeleteScanHistoryItemAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var entity = await _db.ScanHistory.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
        if (entity == null)
            return;
        _db.ScanHistory.Remove(entity);
        await _db.SaveChangesAsync(ct);
    }

    public async Task ClearScanHistoryAsync(Guid userId, CancellationToken ct)
    {
        var rows = await _db.ScanHistory.Where(s => s.UserId == userId).ToListAsync(ct);
        _db.ScanHistory.RemoveRange(rows);
        await _db.SaveChangesAsync(ct);
    }

    public static Guid GetUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? user.FindFirstValue("sub");
        if (!Guid.TryParse(raw, out var id))
            throw new UnauthorizedAccessException("Некорректный пользователь.");
        return id;
    }

    private static ProfileDto ToProfile(ProfileEntity p) =>
        new(p.Id, p.Email, p.CompanyName, p.Inn, p.Phone, p.Role);

    private static SubscriptionDto ToSubscription(SubscriptionEntity s) =>
        new(s.Id, s.UserId, s.PlanId, s.Status, s.CurrentPeriodStart, s.CurrentPeriodEnd,
            s.TrialEndsAt, s.DevicesLimit, s.ProviderSubscriptionId);

    private static PaymentDto ToPayment(PaymentEntity p) =>
        new(p.Id, p.CreatedAt, p.PlanId, p.Amount, p.Currency, p.Status);

    private static DeviceDto ToDevice(DeviceEntity d) =>
        new(d.UserId, d.DeviceId, d.DeviceName, d.Platform, d.CreatedAt, d.LastSeenAt);

    private static TemplateDto ToTemplate(PrintTemplateEntity t) =>
        new(t.Id, t.UserId, t.Name, t.Description, t.WidthMm, t.HeightMm, t.PrinterName,
            t.TemplateData, t.IsDefault, t.CreatedAt, t.UpdatedAt);

    private static ScanHistoryDto ToScan(ScanHistoryEntity s) =>
        new(s.Id, s.UserId, s.RawCode, s.CodeHash, s.Source, s.GsCount, s.HasAi01, s.HasAi21,
            s.HasAi91, s.HasAi92, s.Gtin, s.Serial, s.ScannedAt, s.CreatedAt);
}
