using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DoubleMark.Api.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DoubleMark.Api.Auth;

public sealed class JwtTokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public string CreateAccessToken(
        Guid userId,
        string email,
        Guid? orgId = null,
        string? orgRole = null,
        string? planId = null,
        string? subscriptionStatus = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new(ClaimTypes.NameIdentifier, userId.ToString())
        };
        if (orgId is { } oid)
            claims.Add(new Claim("org_id", oid.ToString()));
        if (!string.IsNullOrWhiteSpace(orgRole))
            claims.Add(new Claim("org_role", orgRole));
        if (!string.IsNullOrWhiteSpace(planId))
            claims.Add(new Claim("plan_id", planId));
        if (!string.IsNullOrWhiteSpace(subscriptionStatus))
            claims.Add(new Claim("subscription_status", subscriptionStatus));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string CreateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }

    public static string HashToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public DateTime AccessExpiresAtUtc() =>
        DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes);

    public DateTime RefreshExpiresAtUtc() =>
        DateTime.UtcNow.AddDays(_options.RefreshTokenDays);
}
