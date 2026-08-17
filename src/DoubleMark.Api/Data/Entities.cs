namespace DoubleMark.Api.Data;

public sealed class UserEntity
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ProfileEntity? Profile { get; set; }
    public SubscriptionEntity? Subscription { get; set; }
    public List<RefreshTokenEntity> RefreshTokens { get; set; } = new();
    public List<PaymentEntity> Payments { get; set; } = new();
    public List<DeviceEntity> Devices { get; set; } = new();
    public List<PrintTemplateEntity> Templates { get; set; } = new();
    public List<ScanHistoryEntity> ScanHistory { get; set; } = new();
}

public sealed class RefreshTokenEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public UserEntity? User { get; set; }
}

public sealed class ProfileEntity
{
    public Guid Id { get; set; }
    public string? Email { get; set; }
    public string? CompanyName { get; set; }
    public string? Inn { get; set; }
    public string? Phone { get; set; }
    public string? Role { get; set; }
    public DateTime UpdatedAt { get; set; }
    public UserEntity? User { get; set; }
}

public sealed class SubscriptionEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string? PlanId { get; set; }
    public string Status { get; set; } = "trialing";
    public DateTime? CurrentPeriodStart { get; set; }
    public DateTime? CurrentPeriodEnd { get; set; }
    public DateTime? TrialEndsAt { get; set; }
    public int DevicesLimit { get; set; } = 3;
    public string? ProviderSubscriptionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public UserEntity? User { get; set; }
}

public sealed class PaymentEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? PlanId { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string? Status { get; set; }
    public UserEntity? User { get; set; }
}

public sealed class DeviceEntity
{
    public string DeviceId { get; set; } = "";
    public Guid UserId { get; set; }
    public string DeviceName { get; set; } = "";
    public string Platform { get; set; } = "Windows";
    public DateTime CreatedAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public UserEntity? User { get; set; }
}

public sealed class PrintTemplateEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public decimal WidthMm { get; set; }
    public decimal HeightMm { get; set; }
    public string? PrinterName { get; set; }
    public string TemplateData { get; set; } = "{}";
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public UserEntity? User { get; set; }
}

public sealed class ScanHistoryEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string RawCode { get; set; } = "";
    public string CodeHash { get; set; } = "";
    public string? Source { get; set; }
    public int? GsCount { get; set; }
    public bool HasAi01 { get; set; }
    public bool HasAi21 { get; set; }
    public bool HasAi91 { get; set; }
    public bool HasAi92 { get; set; }
    public string? Gtin { get; set; }
    public string? Serial { get; set; }
    public DateTime ScannedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public UserEntity? User { get; set; }
}
