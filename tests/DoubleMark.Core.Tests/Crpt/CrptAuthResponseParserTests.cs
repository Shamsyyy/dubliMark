using System.Text.Json;
using DoubleMark.Core.Crpt;
using DoubleMark.Crpt;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

public class CrptAuthResponseParserTests
{
    [Fact]
    public void ParseAuthKey_ReadsUuidAndData()
    {
        const string json = """
            {
              "uuid": "00000000-0000-4000-8000-000000000001",
              "data": "dGVzdC1wYXlsb2Fk"
            }
            """;

        var key = CrptAuthResponseParser.ParseAuthKey(json);

        key.Uuid.Should().Be("00000000-0000-4000-8000-000000000001");
        key.Data.Should().Be("dGVzdC1wYXlsb2Fk");
    }

    [Fact]
    public void ParseSuzToken_UsesTenHourDefaultExpiry()
    {
        const string json = """{ "token": "suz-token-value" }""";
        var issuedAt = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

        var token = CrptAuthResponseParser.ParseSuzToken(json, issuedAt);

        token.Value.Should().Be("suz-token-value");
        token.ExpiresAt.Should().Be(issuedAt.AddHours(10));
        token.IsUnitedUuidToken.Should().BeFalse();
    }

    [Fact]
    public void ParseJwtToken_ReadsExpireDate()
    {
        const string json = """
            {
              "token": "jwt-token-value",
              "expireDate": "2024-06-01T18:00:00+00:00"
            }
            """;

        var token = CrptAuthResponseParser.ParseJwtToken(json);

        token.Value.Should().Be("jwt-token-value");
        token.ExpiresAt.Should().Be(DateTimeOffset.Parse("2024-06-01T18:00:00+00:00"));
        token.IsUnitedUuidToken.Should().BeFalse();
    }

    [Fact]
    public void ParseJwtToken_WithoutExpireDate_UsesTenHourFallback()
    {
        const string json = """{ "token": "jwt-token-value" }""";
        var before = DateTimeOffset.UtcNow;

        var token = CrptAuthResponseParser.ParseJwtToken(json);

        token.Value.Should().Be("jwt-token-value");
        token.ExpiresAt.Should().BeOnOrAfter(before.AddHours(9.9));
        token.ExpiresAt.Should().BeOnOrBefore(before.AddHours(10.1));
    }

    [Fact]
    public void IsPlausibleTokenExpiry_RejectsDefaultDate()
    {
        CrptAuthResponseParser.IsPlausibleTokenExpiry(default).Should().BeFalse();
        CrptAuthResponseParser.IsPlausibleTokenExpiry(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))
            .Should().BeTrue();
    }

    [Fact]
    public void ParseUnitedToken_ReadsUuidToken()
    {
        const string json = """{ "uuidToken": "00000000-0000-4000-8000-000000000099" }""";

        var token = CrptAuthResponseParser.ParseUnitedToken(json);

        token.Value.Should().Be("00000000-0000-4000-8000-000000000099");
        token.IsUnitedUuidToken.Should().BeTrue();
    }

    [Fact]
    public void ParseAuthKey_ThrowsWhenFieldsMissing()
    {
        var act = () => CrptAuthResponseParser.ParseAuthKey("""{ "uuid": "only-uuid" }""");
        act.Should().Throw<InvalidOperationException>();
    }
}
