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
