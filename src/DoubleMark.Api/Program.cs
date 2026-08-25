using System.Text;
using DoubleMark.Api.Admin;
using DoubleMark.Api.Auth;
using DoubleMark.Api.Billing;
using DoubleMark.Api.Data;
using DoubleMark.Api.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<MailOptions>(builder.Configuration.GetSection(MailOptions.SectionName));
builder.Services.Configure<AlphaBankOptions>(builder.Configuration.GetSection(AlphaBankOptions.SectionName));
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
          ?? new JwtOptions();

if (string.IsNullOrWhiteSpace(jwt.SigningKey) || jwt.SigningKey.Length < 32)
    throw new InvalidOperationException("Jwt:SigningKey must be at least 32 characters.");

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());

builder.Services.AddHttpClient<AlphaBankClient>();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton<EmailSender>();
builder.Services.AddScoped<SubscriptionService>();
builder.Services.AddScoped<BillingService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AccountDataService>();
builder.Services.AddScoped<AdminService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
}

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy
            .SetIsOriginAllowed(origin =>
            {
                if (string.IsNullOrWhiteSpace(origin))
                    return false;
                if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                    return false;
                return uri.Host is "localhost" or "127.0.0.1"
                       || uri.Host.Equals("doublemark.ru", StringComparison.OrdinalIgnoreCase)
                       || uri.Host.EndsWith(".doublemark.ru", StringComparison.OrdinalIgnoreCase);
            })
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

var app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json; charset=utf-8";
        var origin = context.Request.Headers.Origin.ToString();
        if (!string.IsNullOrWhiteSpace(origin))
        {
            context.Response.Headers["Access-Control-Allow-Origin"] = origin;
            context.Response.Headers["Access-Control-Allow-Credentials"] = "true";
            context.Response.Headers["Vary"] = "Origin";
        }

        await context.Response.WriteAsJsonAsync(new { error = "Внутренняя ошибка сервера." });
    });
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/", () => Results.Ok(new
{
    service = "DoubleMark.Api",
    health = "/health",
    utc = DateTime.UtcNow
}));
app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "DoubleMark.Api",
    utc = DateTime.UtcNow
}));

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    for (var attempt = 1; attempt <= 20; attempt++)
    {
        try
        {
            if (await db.Database.CanConnectAsync())
                break;
        }
        catch
        {
            if (attempt == 20)
                throw;
        }

        await Task.Delay(500);
    }

    await db.Database.ExecuteSqlRawAsync("""
        ALTER TABLE payments ADD COLUMN IF NOT EXISTS provider text;
        ALTER TABLE payments ADD COLUMN IF NOT EXISTS provider_payment_id text;
        ALTER TABLE user_scan_history ADD COLUMN IF NOT EXISTS org_id uuid;
        CREATE INDEX IF NOT EXISTS ix_payments_provider_payment_id ON payments (provider_payment_id);
        CREATE INDEX IF NOT EXISTS ix_subscriptions_org_id ON subscriptions (org_id);
        CREATE INDEX IF NOT EXISTS ix_user_scan_history_org_id ON user_scan_history (org_id);
        CREATE TABLE IF NOT EXISTS installer_downloads (
            id uuid PRIMARY KEY,
            user_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
            version text NULL,
            file_name text NULL,
            created_at timestamptz NOT NULL DEFAULT now()
        );
        CREATE INDEX IF NOT EXISTS ix_installer_downloads_user_id ON installer_downloads (user_id);
        """);

    var subscriptions = scope.ServiceProvider.GetRequiredService<SubscriptionService>();
    await subscriptions.BackfillOrgIdsAsync(CancellationToken.None);
}

app.Run();

public partial class Program;
