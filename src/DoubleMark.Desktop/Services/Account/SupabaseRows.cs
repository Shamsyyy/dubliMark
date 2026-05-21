using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace DoubleMark.Desktop.Services.Account;

[Table("profiles")]
public sealed class ProfileRow : BaseModel
{
    [PrimaryKey("id", false)]
    public string Id { get; set; } = "";

    [Column("email")]
    public string? Email { get; set; }

    [Column("company_name")]
    public string? CompanyName { get; set; }

    [Column("inn")]
    public string? Inn { get; set; }

    [Column("phone")]
    public string? Phone { get; set; }

    [Column("role")]
    public string? Role { get; set; }
}

[Table("subscriptions")]
public sealed class SubscriptionRow : BaseModel
{
    [PrimaryKey("id", false)]
    public string Id { get; set; } = "";

    [Column("user_id")]
    public string UserId { get; set; } = "";

    [Column("plan_id")]
    public string? PlanId { get; set; }

    [Column("status")]
    public string? Status { get; set; }

    [Column("current_period_start")]
    public DateTime? CurrentPeriodStart { get; set; }

    [Column("current_period_end")]
    public DateTime? CurrentPeriodEnd { get; set; }

    [Column("trial_ends_at")]
    public DateTime? TrialEndsAt { get; set; }

    [Column("devices_limit")]
    public int? DevicesLimit { get; set; }

    [Column("provider_subscription_id")]
    public string? ProviderSubscriptionId { get; set; }
}

[Table("payments")]
public sealed class PaymentRow : BaseModel
{
    [PrimaryKey("id", false)]
    public string Id { get; set; } = "";

    [Column("user_id")]
    public string UserId { get; set; } = "";

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("plan_id")]
    public string? PlanId { get; set; }

    [Column("amount")]
    public decimal? Amount { get; set; }

    [Column("currency")]
    public string? Currency { get; set; }

    [Column("status")]
    public string? Status { get; set; }
}

[Table("user_devices")]
public sealed class DeviceRow : BaseModel
{
    [PrimaryKey("device_id", false)]
    public string DeviceId { get; set; } = "";

    [Column("user_id")]
    public string UserId { get; set; } = "";

    [Column("device_name")]
    public string DeviceName { get; set; } = "";

    [Column("platform")]
    public string Platform { get; set; } = "";

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("last_seen_at")]
    public DateTime? LastSeenAt { get; set; }
}

[Table("user_print_templates")]
public sealed class UserPrintTemplateRow : BaseModel
{
    [PrimaryKey("id", false)]
    public string Id { get; set; } = "";

    [Column("user_id")]
    public string UserId { get; set; } = "";

    [Column("name")]
    public string Name { get; set; } = "";

    [Column("description")]
    public string? Description { get; set; }

    [Column("width_mm")]
    public decimal WidthMm { get; set; }

    [Column("height_mm")]
    public decimal HeightMm { get; set; }

    [Column("printer_name")]
    public string? PrinterName { get; set; }

    [Column("template_data")]
    public object TemplateData { get; set; } = "{}";

    [Column("is_default")]
    public bool IsDefault { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("user_scan_history")]
public sealed class UserScanHistoryRow : BaseModel
{
    [PrimaryKey("id", false)]
    public string Id { get; set; } = "";

    [Column("user_id")]
    public string UserId { get; set; } = "";

    [Column("raw_code")]
    public string RawCode { get; set; } = "";

    [Column("code_hash")]
    public string CodeHash { get; set; } = "";

    [Column("source")]
    public string? Source { get; set; }

    [Column("gs_count")]
    public int? GsCount { get; set; }

    [Column("has_ai01")]
    public bool HasAi01 { get; set; }

    [Column("has_ai21")]
    public bool HasAi21 { get; set; }

    [Column("has_ai91")]
    public bool HasAi91 { get; set; }

    [Column("has_ai92")]
    public bool HasAi92 { get; set; }

    [Column("gtin")]
    public string? Gtin { get; set; }

    [Column("serial")]
    public string? Serial { get; set; }

    [Column("scanned_at")]
    public DateTime? ScannedAt { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }
}
