using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DoubleMark.Crpt;

/// <summary>
/// Parses True API auth JSON responses without HTTP (spec §14.1).
/// </summary>
public static class CrptAuthResponseParser
{
    public static CrptAuthKey ParseAuthKey(string json)
    {
        var key = JsonSerializer.Deserialize<AuthKeyResponse>(json, CrptJson.Api)
            ?? throw new InvalidOperationException("auth/key returned empty body");

        if (string.IsNullOrWhiteSpace(key.Uuid) || string.IsNullOrWhiteSpace(key.Data))
            throw new InvalidOperationException("auth/key response missing uuid or data");

        return new CrptAuthKey(key.Uuid, key.Data);
    }

    public static CrptAuthToken ParseSuzToken(string json, DateTimeOffset? issuedAt = null)
    {
        var token = JsonSerializer.Deserialize<SuzAuthResponse>(json, CrptJson.Api)
            ?? throw new InvalidOperationException("SUZ simpleSignIn returned empty token");

        if (string.IsNullOrWhiteSpace(token.Token))
            throw new InvalidOperationException("SUZ simpleSignIn response missing token");

        var issued = issuedAt ?? DateTimeOffset.UtcNow;
        return new CrptAuthToken(token.Token, issued.AddHours(10), IsUnitedUuidToken: false);
    }

    public static CrptAuthToken ParseJwtToken(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tokenValue = ReadTokenValue(root);
        if (string.IsNullOrWhiteSpace(tokenValue))
            throw new InvalidOperationException("JWT simpleSignIn response missing token");

        var expiresAt = ReadExpireDate(root) ?? DateTimeOffset.UtcNow.AddHours(10);
        return new CrptAuthToken(tokenValue, expiresAt, IsUnitedUuidToken: false);
    }

    /// <summary>True API JWT responses often omit <c>expireDate</c>; reject default/unparseable values.</summary>
    public static bool IsPlausibleTokenExpiry(DateTimeOffset expiresAt) =>
        expiresAt.Year >= 2000;

    private static string? ReadTokenValue(JsonElement root) =>
        root.TryGetProperty("token", out var tokenEl) && tokenEl.ValueKind == JsonValueKind.String
            ? tokenEl.GetString()
            : null;

    private static DateTimeOffset? ReadExpireDate(JsonElement root)
    {
        foreach (var propertyName in new[] { "expireDate", "expire_date", "expiresAt", "expires_at" })
        {
            if (!root.TryGetProperty(propertyName, out var element))
                continue;

            if (element.ValueKind == JsonValueKind.String)
            {
                var text = element.GetString();
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (DateTimeOffset.TryParse(
                        text,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var parsed) &&
                    IsPlausibleTokenExpiry(parsed))
                    return parsed;
            }
            else if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var unixMs))
            {
                var fromMs = DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
                if (IsPlausibleTokenExpiry(fromMs))
                    return fromMs;
            }
        }

        return null;
    }

    public static CrptAuthToken ParseUnitedToken(string json)
    {
        var token = JsonSerializer.Deserialize<UnitedTokenAuthResponse>(json, CrptJson.Api)
            ?? throw new InvalidOperationException("United token simpleSignIn returned empty body");

        if (string.IsNullOrWhiteSpace(token.UuidToken))
            throw new InvalidOperationException("United token simpleSignIn response missing uuidToken");

        return new CrptAuthToken(token.UuidToken, DateTimeOffset.UtcNow.AddHours(10), IsUnitedUuidToken: true);
    }

    private sealed record AuthKeyResponse(
        [property: JsonPropertyName("uuid")] string Uuid,
        [property: JsonPropertyName("data")] string Data);

    private sealed record SuzAuthResponse([property: JsonPropertyName("token")] string Token);

    private sealed record UnitedTokenAuthResponse(
        [property: JsonPropertyName("uuidToken")] string UuidToken);
}

public sealed record CrptAuthKey(string Uuid, string Data);
