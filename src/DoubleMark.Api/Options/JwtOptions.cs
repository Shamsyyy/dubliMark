namespace DoubleMark.Api.Options;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "DoubleMark";
    public string Audience { get; set; } = "DoubleMark.Desktop";
    public string SigningKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 60;
    public int RefreshTokenDays { get; set; } = 30;
}
