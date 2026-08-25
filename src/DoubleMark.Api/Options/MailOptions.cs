namespace DoubleMark.Api.Options;

public sealed class MailOptions
{
    public const string SectionName = "Mail";

    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public string SmtpUser { get; set; } = "";
    public string SmtpPass { get; set; } = "";
    public string MailFrom { get; set; } = "";
    public string SiteBaseUrl { get; set; } = "https://doublemark.ru";
    public bool SmtpSecure { get; set; } = true;
}
