using DoubleMark.Api.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace DoubleMark.Api.Auth;

public sealed class EmailSender
{
    private readonly MailOptions _options;

    public EmailSender(IOptions<MailOptions> options)
    {
        _options = options.Value;
    }

    public void EnsureConfigured()
    {
        ValidateConfiguration();
    }

    public async Task SendRegistrationConfirmationAsync(string email, string token, CancellationToken ct)
    {
        var confirmUrl = BuildSiteUrl($"/confirm-email?token={Uri.EscapeDataString(token)}");
        var subject = "DoubleMark: подтверждение email";
        var plainBody = BuildPlainBody(
            "Вы зарегистрировали аккаунт DoubleMark.",
            "Чтобы завершить регистрацию, откройте ссылку:",
            confirmUrl,
            "Ссылка действует 24 часа.");
        var htmlBody = BuildHtmlBody(
            "Подтверждение email",
            "Вы зарегистрировали аккаунт DoubleMark. Чтобы завершить регистрацию, откройте ссылку ниже.",
            confirmUrl,
            "Ссылка действует 24 часа.");

        await SendAsync(email, subject, plainBody, htmlBody, ct);
    }

    public async Task SendPasswordResetAsync(string email, string token, CancellationToken ct)
    {
        var resetUrl = BuildSiteUrl($"/update-password?token={Uri.EscapeDataString(token)}");
        var subject = "DoubleMark: смена пароля";
        var plainBody = BuildPlainBody(
            "Для вашего аккаунта DoubleMark запросили смену пароля.",
            "Откройте ссылку, чтобы задать новый пароль:",
            resetUrl,
            "Ссылка действует 1 час.");
        var htmlBody = BuildHtmlBody(
            "Смена пароля",
            "Для вашего аккаунта DoubleMark запросили смену пароля. Откройте ссылку ниже, чтобы задать новый пароль.",
            resetUrl,
            "Ссылка действует 1 час.");

        await SendAsync(email, subject, plainBody, htmlBody, ct);
    }

    private async Task SendAsync(
        string to,
        string subject,
        string plainBody,
        string htmlBody,
        CancellationToken ct)
    {
        ValidateConfiguration();

        var from = GetFromAddress();
        var domain = from.Contains('@') ? from[(from.IndexOf('@') + 1)..] : "doublemark.ru";
        var message = new MimeMessage();
        message.MessageId = $"{Guid.NewGuid():N}@{domain}";
        message.Date = DateTimeOffset.Now;
        message.From.Add(new MailboxAddress("DoubleMark", from));
        message.Sender = new MailboxAddress("DoubleMark", from);
        message.To.Add(MailboxAddress.Parse(to));
        message.ReplyTo.Add(new MailboxAddress("DoubleMark", from));
        message.Subject = subject;
        message.Headers.Add("Auto-Submitted", "auto-generated");
        message.Headers.Add("X-Auto-Response-Suppress", "All");
        message.Headers.Add("List-Unsubscribe", $"<mailto:{from}>");
        message.Body = new BodyBuilder
        {
            TextBody = plainBody,
            HtmlBody = htmlBody
        }.ToMessageBody();

        using var client = new SmtpClient();
        client.Timeout = 20000;
        client.CheckCertificateRevocation = false;

        var secureSocketOptions = _options.SmtpSecure
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTls;

        await client.ConnectAsync(_options.SmtpHost, _options.SmtpPort, secureSocketOptions, ct);
        await client.AuthenticateAsync(_options.SmtpUser, _options.SmtpPass, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.SmtpHost)
            || string.IsNullOrWhiteSpace(_options.SmtpUser)
            || string.IsNullOrWhiteSpace(_options.SmtpPass)
            || string.IsNullOrWhiteSpace(_options.MailFrom))
        {
            throw new InvalidOperationException(
                "Отправка email не настроена. Заполните Mail:SmtpHost, Mail:SmtpUser, Mail:SmtpPass и Mail:MailFrom.");
        }
    }

    private string BuildSiteUrl(string relativePath)
    {
        var baseUrl = (_options.SiteBaseUrl ?? "https://doublemark.ru").Trim().TrimEnd('/');
        return $"{baseUrl}{relativePath}";
    }

    private string GetFromAddress()
    {
        return _options.MailFrom.Trim();
    }

    private string BuildPlainBody(string intro, string linkLead, string url, string ttl)
    {
        return $"""
            Здравствуйте!

            {intro}
            {linkLead}
            {url}

            {ttl}
            Если это были не вы, проигнорируйте письмо.

            DoubleMark
            {GetFromAddress()}
            ИП Гатауллина Диана Наилевна
            """;
    }

    private string BuildHtmlBody(string title, string intro, string url, string ttl)
    {
        return $"""
            <html>
              <body style="margin:0;padding:16px;background:#ffffff;color:#1a1a1a;font-family:Arial,sans-serif;font-size:16px;line-height:1.5;">
                <p><strong>{title}</strong></p>
                <p>Здравствуйте!</p>
                <p>{intro}</p>
                <p><a href="{url}">{url}</a></p>
                <p>{ttl}</p>
                <p>Если это были не вы, проигнорируйте письмо.</p>
                <p>DoubleMark<br>{GetFromAddress()}<br>ИП Гатауллина Диана Наилевна</p>
              </body>
            </html>
            """;
    }
}
