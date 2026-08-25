using System.Security.Cryptography;
using DoubleMark.Api.Billing;
using DoubleMark.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DoubleMark.Api.Auth;

public sealed record AuthTokensDto(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAtUtc,
    Guid UserId,
    string Email);
public sealed record RegisterResponseDto(
    bool NeedsEmailConfirmation,
    string Message);
public sealed record AuthActionMessageDto(
    bool Ok,
    string Message);

public sealed record RegisterRequest(
    string Email,
    string Password,
    string? CompanyName = null,
    string? Inn = null,
    string? Phone = null,
    bool? PersonalDataConsent = null,
    string? PersonalDataConsentVersion = null);
public sealed record LoginRequest(string Email, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record ConfirmEmailRequest(string Token);
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Token, string Password);
public sealed record ResendConfirmationRequest(string Email);

public sealed class AuthService
{
    private const string ConfirmEmailTokenType = "confirm_email";
    private const string ResetPasswordTokenType = "reset_password";

    private readonly AppDbContext _db;
    private readonly JwtTokenService _jwt;
    private readonly EmailSender _emailSender;
    private readonly SubscriptionService _subscriptions;

    public AuthService(
        AppDbContext db,
        JwtTokenService jwt,
        EmailSender emailSender,
        SubscriptionService subscriptions)
    {
        _db = db;
        _jwt = jwt;
        _emailSender = emailSender;
        _subscriptions = subscriptions;
    }

    public async Task<RegisterResponseDto> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        _emailSender.EnsureConfigured();

        var email = NormalizeEmail(request.Email);
        ValidatePassword(request.Password);

        if (await _db.Users.AnyAsync(u => u.Email == email, ct))
            throw new InvalidOperationException("Пользователь с таким email уже существует.");

        var now = DateTime.UtcNow;
        var inn = string.IsNullOrWhiteSpace(request.Inn) ? null : request.Inn.Replace(" ", "");
        var company = string.IsNullOrWhiteSpace(request.CompanyName)
            ? email
            : request.CompanyName.Trim();

        var org = await _db.Organizations.FirstOrDefaultAsync(o => o.Inn == inn, ct);

        if (org == null)
        {
            org = new OrganizationEntity
            {
                Id = Guid.NewGuid(),
                LegalName = company,
                Inn = inn,
                Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
                Email = email,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.Organizations.Add(org);
            await _db.SaveChangesAsync(ct);
        }

        var hasMembers = await _db.Profiles.AnyAsync(p => p.OrgId == org.Id, ct);
        var orgRole = hasMembers ? "operator" : "owner";

        var basePlan = PlanCatalog.GetByTier(PlanTier.Base)!;
        if (!await _db.Entitlements.AnyAsync(e => e.OrgId == org.Id, ct))
        {
            _db.Entitlements.Add(new EntitlementEntity
            {
                OrgId = org.Id,
                CanDownload = true,
                DevicesLimit = basePlan.DevicesLimit,
                UpdatedAt = now
            });
        }

        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            EmailConfirmedAt = null,
            CreatedAt = now,
            UpdatedAt = now
        };

        var profile = new ProfileEntity
        {
            Id = user.Id,
            Email = email,
            CompanyName = string.IsNullOrWhiteSpace(request.CompanyName) ? null : request.CompanyName.Trim(),
            Inn = inn,
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            Role = "user",
            OrgId = org.Id,
            OrgRole = orgRole,
            UpdatedAt = now
        };

        _db.Users.Add(user);
        _db.Profiles.Add(profile);
        if (request.PersonalDataConsent == true)
        {
            _db.PersonalDataConsents.Add(new PersonalDataConsentEntity
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                ConsentVersion = string.IsNullOrWhiteSpace(request.PersonalDataConsentVersion)
                    ? "unspecified"
                    : request.PersonalDataConsentVersion.Trim(),
                AcceptedAtUtc = now
            });
        }
        await _db.SaveChangesAsync(ct);

        await _subscriptions.EnsureTrialForNewOrgAsync(org.Id, user.Id, ct);

        var confirmationToken = await IssueAuthActionTokenAsync(user.Id, ConfirmEmailTokenType, TimeSpan.FromHours(24), ct);
        await _emailSender.SendRegistrationConfirmationAsync(user.Email, confirmationToken, ct);

        return new RegisterResponseDto(
            true,
            "Проверьте почту и подтвердите email, чтобы завершить регистрацию.");
    }

    public async Task<AuthTokensDto> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var email = NormalizeEmail(request.Email);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Неверный email или пароль.");
        if (user.EmailConfirmedAt == null)
            throw new UnauthorizedAccessException("Подтвердите email, чтобы войти в аккаунт.");

        return await IssueTokensAsync(user, ct);
    }

    public async Task<AuthTokensDto> RefreshAsync(RefreshRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            throw new UnauthorizedAccessException("Refresh token отсутствует.");

        var hash = JwtTokenService.HashToken(request.RefreshToken);
        var stored = await _db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored?.User == null || stored.RevokedAt != null || stored.ExpiresAt < DateTime.UtcNow)
            throw new UnauthorizedAccessException("Refresh token недействителен.");

        stored.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return await IssueTokensAsync(stored.User, ct);
    }

    public async Task LogoutAsync(Guid userId, string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return;

        var hash = JwtTokenService.HashToken(refreshToken);
        var stored = await _db.RefreshTokens
            .FirstOrDefaultAsync(t => t.UserId == userId && t.TokenHash == hash, ct);
        if (stored == null)
            return;

        stored.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<AuthTokensDto> ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken ct)
    {
        var token = GetRequiredToken(request.Token);
        var hash = JwtTokenService.HashToken(token);
        var now = DateTime.UtcNow;
        var stored = await _db.AuthActionTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(
                t => t.TokenHash == hash
                     && t.TokenType == ConfirmEmailTokenType,
                ct);

        if (stored?.User == null || stored.UsedAt != null || stored.ExpiresAt < now)
            throw new UnauthorizedAccessException("Ссылка подтверждения недействительна или истекла.");

        stored.UsedAt = now;
        stored.User.EmailConfirmedAt ??= now;
        stored.User.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        return await IssueTokensAsync(stored.User, ct);
    }

    public async Task<AuthActionMessageDto> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct)
    {
        _emailSender.EnsureConfigured();

        var email = NormalizeEmail(request.Email);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user == null)
        {
            return GenericPasswordResetMessage();
        }

        if (await HasRecentAuthActionAsync(user.Id, ResetPasswordTokenType, TimeSpan.FromMinutes(15), ct))
            return GenericPasswordResetMessage();

        var token = await IssueAuthActionTokenAsync(user.Id, ResetPasswordTokenType, TimeSpan.FromHours(1), ct);
        await _emailSender.SendPasswordResetAsync(user.Email, token, ct);
        return GenericPasswordResetMessage();
    }

    public async Task<AuthActionMessageDto> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct)
    {
        ValidatePassword(request.Password);

        var token = GetRequiredToken(request.Token);
        var hash = JwtTokenService.HashToken(token);
        var now = DateTime.UtcNow;
        var stored = await _db.AuthActionTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(
                t => t.TokenHash == hash
                     && t.TokenType == ResetPasswordTokenType,
                ct);

        if (stored?.User == null || stored.UsedAt != null || stored.ExpiresAt < now)
            throw new UnauthorizedAccessException("Ссылка сброса пароля недействительна или истекла.");

        stored.UsedAt = now;
        stored.User.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        stored.User.UpdatedAt = now;
        await RevokeAllRefreshTokensAsync(stored.User.Id, now, ct);
        await _db.SaveChangesAsync(ct);

        return new AuthActionMessageDto(true, "Пароль обновлён. Теперь можно войти с новым паролем.");
    }

    public async Task<AuthActionMessageDto> ResendConfirmationAsync(ResendConfirmationRequest request, CancellationToken ct)
    {
        _emailSender.EnsureConfigured();

        var email = NormalizeEmail(request.Email);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user == null || user.EmailConfirmedAt != null)
        {
            return new AuthActionMessageDto(true, "Если аккаунт существует, письмо с подтверждением отправлено.");
        }

        if (await HasRecentAuthActionAsync(user.Id, ConfirmEmailTokenType, TimeSpan.FromMinutes(15), ct))
            return new AuthActionMessageDto(true, "Если аккаунт существует, письмо с подтверждением отправлено.");

        var token = await IssueAuthActionTokenAsync(user.Id, ConfirmEmailTokenType, TimeSpan.FromHours(24), ct);
        await _emailSender.SendRegistrationConfirmationAsync(user.Email, token, ct);

        return new AuthActionMessageDto(true, "Если аккаунт существует, письмо с подтверждением отправлено.");
    }

    /// <summary>Admin-triggered password reset email (always sends when user exists).</summary>
    public async Task<AuthActionMessageDto> AdminSendPasswordResetAsync(Guid userId, CancellationToken ct)
    {
        _emailSender.EnsureConfigured();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new KeyNotFoundException("Пользователь не найден.");

        var token = await IssueAuthActionTokenAsync(user.Id, ResetPasswordTokenType, TimeSpan.FromHours(1), ct);
        await _emailSender.SendPasswordResetAsync(user.Email, token, ct);
        return new AuthActionMessageDto(true, $"Письмо для сброса пароля отправлено на {user.Email}.");
    }

    /// <summary>Admin-triggered confirmation email.</summary>
    public async Task<AuthActionMessageDto> AdminResendConfirmationAsync(Guid userId, CancellationToken ct)
    {
        _emailSender.EnsureConfigured();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new KeyNotFoundException("Пользователь не найден.");

        if (user.EmailConfirmedAt != null)
            return new AuthActionMessageDto(true, $"Email {user.Email} уже подтверждён.");

        var token = await IssueAuthActionTokenAsync(user.Id, ConfirmEmailTokenType, TimeSpan.FromHours(24), ct);
        await _emailSender.SendRegistrationConfirmationAsync(user.Email, token, ct);
        return new AuthActionMessageDto(true, $"Письмо подтверждения отправлено на {user.Email}.");
    }

    private async Task<AuthTokensDto> IssueTokensAsync(UserEntity user, CancellationToken ct)
    {
        var profile = await _db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == user.Id, ct);
        var subscription = await _subscriptions.GetSubscriptionForUserAsync(user.Id, ct);
        var access = _jwt.CreateAccessToken(
            user.Id,
            user.Email,
            profile?.OrgId,
            profile?.OrgRole,
            subscription?.PlanId,
            subscription?.Status);
        var refresh = _jwt.CreateRefreshToken();
        var accessExpires = _jwt.AccessExpiresAtUtc();

        var refreshEntity = new RefreshTokenEntity
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = JwtTokenService.HashToken(refresh),
            ExpiresAt = _jwt.RefreshExpiresAtUtc(),
            CreatedAt = DateTime.UtcNow
        };
        _db.RefreshTokens.Add(refreshEntity);
        await _db.SaveChangesAsync(ct);

        return new AuthTokensDto(access, refresh, accessExpires, user.Id, user.Email);
    }

    private async Task<bool> HasRecentAuthActionAsync(
        Guid userId,
        string tokenType,
        TimeSpan window,
        CancellationToken ct)
    {
        var since = DateTime.UtcNow.Subtract(window);
        return await _db.AuthActionTokens.AnyAsync(
            t => t.UserId == userId && t.TokenType == tokenType && t.CreatedAt >= since,
            ct);
    }

    private async Task<string> IssueAuthActionTokenAsync(
        Guid userId,
        string tokenType,
        TimeSpan lifetime,
        CancellationToken ct)
    {
        var existing = await _db.AuthActionTokens
            .Where(t => t.UserId == userId && t.TokenType == tokenType && t.UsedAt == null)
            .ToListAsync(ct);
        if (existing.Count > 0)
        {
            _db.AuthActionTokens.RemoveRange(existing);
        }

        var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        _db.AuthActionTokens.Add(new AuthActionTokenEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenType = tokenType,
            TokenHash = JwtTokenService.HashToken(rawToken),
            ExpiresAt = DateTime.UtcNow.Add(lifetime),
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
        return rawToken;
    }

    private async Task RevokeAllRefreshTokensAsync(Guid userId, DateTime revokedAtUtc, CancellationToken ct)
    {
        var tokens = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in tokens)
        {
            token.RevokedAt = revokedAtUtc;
        }
    }

    private static AuthActionMessageDto GenericPasswordResetMessage()
    {
        return new AuthActionMessageDto(
            true,
            "Если аккаунт с таким email существует, мы отправили ссылку для сброса пароля.");
    }

    private static string GetRequiredToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("Токен обязателен.");
        return token.Trim();
    }

    private static string NormalizeEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email обязателен.");
        return email.Trim().ToLowerInvariant();
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            throw new ArgumentException("Пароль должен быть не короче 8 символов.");
    }
}
