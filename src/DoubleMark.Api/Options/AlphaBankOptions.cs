namespace DoubleMark.Api.Options;

public sealed class AlphaBankOptions
{
    public const string SectionName = "AlphaBank";

    /// <summary>Merchant login from Alfa acquiring cabinet.</summary>
    public string UserName { get; set; } = "";

    /// <summary>Merchant password.</summary>
    public string Password { get; set; } = "";

    /// <summary>API base, e.g. https://pay.alfabank.ru/payment/rest/</summary>
    public string ApiBaseUrl { get; set; } = "https://pay.alfabank.ru/payment/rest/";

    /// <summary>Return URL after payment (success/fail page on site).</summary>
    public string ReturnUrl { get; set; } = "https://doublemark.ru/checkout/result";

    /// <summary>Optional shared secret for webhook HMAC (if configured).</summary>
    public string WebhookSecret { get; set; } = "";

    /// <summary>
    /// When true or credentials empty, checkout creates a pending payment
    /// and returns a local sandbox confirmation URL instead of bank form.
    /// </summary>
    public bool SandboxMode { get; set; } = true;

    public bool IsConfigured =>
        !SandboxMode
        && !string.IsNullOrWhiteSpace(UserName)
        && !string.IsNullOrWhiteSpace(Password);
}
