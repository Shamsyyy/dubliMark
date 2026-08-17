using Microsoft.EntityFrameworkCore;

namespace DoubleMark.Api.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<RefreshTokenEntity> RefreshTokens => Set<RefreshTokenEntity>();
    public DbSet<ProfileEntity> Profiles => Set<ProfileEntity>();
    public DbSet<SubscriptionEntity> Subscriptions => Set<SubscriptionEntity>();
    public DbSet<PaymentEntity> Payments => Set<PaymentEntity>();
    public DbSet<DeviceEntity> Devices => Set<DeviceEntity>();
    public DbSet<PrintTemplateEntity> PrintTemplates => Set<PrintTemplateEntity>();
    public DbSet<ScanHistoryEntity> ScanHistory => Set<ScanHistoryEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserEntity>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.PasswordHash).HasColumnName("password_hash");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<RefreshTokenEntity>(e =>
        {
            e.ToTable("refresh_tokens");
            e.HasKey(x => x.Id);
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.TokenHash).HasColumnName("token_hash");
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.RevokedAt).HasColumnName("revoked_at");
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne(x => x.User).WithMany(x => x.RefreshTokens).HasForeignKey(x => x.UserId);
        });

        modelBuilder.Entity<ProfileEntity>(e =>
        {
            e.ToTable("profiles");
            e.HasKey(x => x.Id);
            e.Property(x => x.CompanyName).HasColumnName("company_name");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasOne(x => x.User).WithOne(x => x.Profile).HasForeignKey<ProfileEntity>(x => x.Id);
        });

        modelBuilder.Entity<SubscriptionEntity>(e =>
        {
            e.ToTable("subscriptions");
            e.HasKey(x => x.Id);
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.PlanId).HasColumnName("plan_id");
            e.Property(x => x.CurrentPeriodStart).HasColumnName("current_period_start");
            e.Property(x => x.CurrentPeriodEnd).HasColumnName("current_period_end");
            e.Property(x => x.TrialEndsAt).HasColumnName("trial_ends_at");
            e.Property(x => x.DevicesLimit).HasColumnName("devices_limit");
            e.Property(x => x.ProviderSubscriptionId).HasColumnName("provider_subscription_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => x.UserId).IsUnique();
            e.HasOne(x => x.User).WithOne(x => x.Subscription).HasForeignKey<SubscriptionEntity>(x => x.UserId);
        });

        modelBuilder.Entity<PaymentEntity>(e =>
        {
            e.ToTable("payments");
            e.HasKey(x => x.Id);
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.PlanId).HasColumnName("plan_id");
            e.HasOne(x => x.User).WithMany(x => x.Payments).HasForeignKey(x => x.UserId);
        });

        modelBuilder.Entity<DeviceEntity>(e =>
        {
            e.ToTable("user_devices");
            e.HasKey(x => new { x.UserId, x.DeviceId });
            e.Property(x => x.DeviceId).HasColumnName("device_id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.DeviceName).HasColumnName("device_name");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.LastSeenAt).HasColumnName("last_seen_at");
            e.HasOne(x => x.User).WithMany(x => x.Devices).HasForeignKey(x => x.UserId);
        });

        modelBuilder.Entity<PrintTemplateEntity>(e =>
        {
            e.ToTable("user_print_templates");
            e.HasKey(x => x.Id);
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.WidthMm).HasColumnName("width_mm");
            e.Property(x => x.HeightMm).HasColumnName("height_mm");
            e.Property(x => x.PrinterName).HasColumnName("printer_name");
            e.Property(x => x.TemplateData).HasColumnName("template_data").HasColumnType("jsonb");
            e.Property(x => x.IsDefault).HasColumnName("is_default");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasOne(x => x.User).WithMany(x => x.Templates).HasForeignKey(x => x.UserId);
        });

        modelBuilder.Entity<ScanHistoryEntity>(e =>
        {
            e.ToTable("user_scan_history");
            e.HasKey(x => x.Id);
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.RawCode).HasColumnName("raw_code");
            e.Property(x => x.CodeHash).HasColumnName("code_hash");
            e.Property(x => x.GsCount).HasColumnName("gs_count");
            e.Property(x => x.HasAi01).HasColumnName("has_ai01");
            e.Property(x => x.HasAi21).HasColumnName("has_ai21");
            e.Property(x => x.HasAi91).HasColumnName("has_ai91");
            e.Property(x => x.HasAi92).HasColumnName("has_ai92");
            e.Property(x => x.ScannedAt).HasColumnName("scanned_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasOne(x => x.User).WithMany(x => x.ScanHistory).HasForeignKey(x => x.UserId);
        });
    }
}
