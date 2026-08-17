using DoubleMark.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DoubleMark.Api.Auth;

public sealed record AuthTokensDto(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAtUtc,
    Guid UserId,
    string Email);

public sealed record RegisterRequest(
    string Email,
    string Password,
    string? CompanyName = null,
    string? Inn = null,
    string? Phone = null);
public sealed record LoginRequest(string Email, string Password);
public sealed record RefreshRequest(string RefreshToken);

public sealed class AuthService
{
    private readonly AppDbContext _db;
    private readonly JwtTokenService _jwt;

    public AuthService(AppDbContext db, JwtTokenService jwt)
    {
        _db = db;
        _jwt = jwt;
    }

    public async Task<AuthTokensDto> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var email = NormalizeEmail(request.Email);
        ValidatePassword(request.Password);

        if (await _db.Users.AnyAsync(u => u.Email == email, ct))
            throw new InvalidOperationException("Пользователь с таким email уже существует.");

        var now = DateTime.UtcNow;
        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            CreatedAt = now,
            UpdatedAt = now
        };

        var profile = new ProfileEntity
        {
            Id = user.Id,
            Email = email,
            CompanyName = string.IsNullOrWhiteSpace(request.CompanyName) ? null : request.CompanyName.Trim(),
            Inn = string.IsNullOrWhiteSpace(request.Inn) ? null : request.Inn.Replace(" ", ""),
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            Role = "user",
            UpdatedAt = now
        };

        var subscription = new SubscriptionEntity
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            PlanId = "local-dev",
            Status = "active",
            CurrentPeriodStart = now,
            CurrentPeriodEnd = now.AddYears(1),
            TrialEndsAt = now.AddDays(30),
            DevicesLimit = 5,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.Users.Add(user);
        _db.Profiles.Add(profile);
        _db.Subscriptions.Add(subscription);
        await _db.SaveChangesAsync(ct);

        return await IssueTokensAsync(user, ct);
    }

    public async Task<AuthTokensDto> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var email = NormalizeEmail(request.Email);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Неверный email или пароль.");

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

    private async Task<AuthTokensDto> IssueTokensAsync(UserEntity user, CancellationToken ct)
    {
        var access = _jwt.CreateAccessToken(user.Id, user.Email);
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

    private static string NormalizeEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email обязателен.");
        return email.Trim().ToLowerInvariant();
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
            throw new ArgumentException("Пароль должен быть не короче 6 символов.");
    }
}
