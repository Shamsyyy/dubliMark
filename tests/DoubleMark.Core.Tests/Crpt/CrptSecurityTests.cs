using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DoubleMark.Crpt;
using DoubleMark.Desktop.Services;
using DoubleMark.Desktop.Services.Crpt;
using DoubleMark.Desktop.Settings;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

public class CrptSecurityTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly CrptSettingsStore _store;

    public CrptSecurityTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "DoubleMark.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _store = new CrptSettingsStore(_tempDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public void CrptLogRedactor_MasksGs1LikePayloads()
    {
        var payload = CrptTestFixtures.SyntheticMarkingCode(1);

        var redacted = CrptLogRedactor.Redact($"download failed rawPayload={payload}");

        redacted.Should().NotContain("SYNTHETICPAYLOAD");
        redacted.Should().NotContain(((char)0x1D).ToString());
        redacted.Should().NotContain("91EE12");
        redacted.Should().Contain("[redacted]");
    }

    [Fact]
    public void CrptLogRedactor_MasksJwtTokens()
    {
        var jwt = CrptTestFixtures.SyntheticJwt(7);

        var redacted = CrptLogRedactor.Redact($"Authorization: Bearer {jwt}");

        redacted.Should().NotContain(jwt);
        redacted.Should().Contain("[jwt-redacted]");
    }

    [Fact]
    public void CrptLogRedactor_RedactApiErrorBody_DoesNotEchoMarkingCodes()
    {
        var code = CrptTestFixtures.SyntheticMarkingCode(2);

        var redacted = CrptLogRedactor.RedactApiErrorBody(code + " invalid payload");

        redacted.Should().NotContain("SYNTHETICPAYLOAD");
        redacted.Should().Be("Upstream API rejected the request (response redacted).");
    }

    [Fact]
    public void LoggingService_RedactsCrptTokensAndPayloads()
    {
        var jwt = CrptTestFixtures.SyntheticJwt(3);
        var message = $"token={jwt} rawPayload={CrptTestFixtures.SyntheticPayload}";

        var method = typeof(LoggingService).GetMethod("BuildSafeDiagnosticReport", BindingFlags.Public | BindingFlags.Static);
        method.Should().NotBeNull();

        var snapshot = new AppSettingsSnapshot(
            "HID", null, 9600, false, Array.Empty<string>(), false, null, null, "Single", false,
            null, 0, 0, false, false, false, false);

        var report = LoggingService.BuildSafeDiagnosticReport("test", snapshot, maxLogLines: 0);
        _ = report;

        var redact = typeof(LoggingService)
            .GetMethod("RedactSensitive", BindingFlags.NonPublic | BindingFlags.Static);
        redact.Should().NotBeNull();

        var redacted = (string)redact!.Invoke(null, [message])!;
        redacted.Should().NotContain(jwt);
        redacted.Should().NotContain(CrptTestFixtures.SyntheticPayload);
    }

    [Fact]
    public void CrptSecurityGuard_RejectsSecretKeysInPlainSettingsJson()
    {
        const string json = """{"inn":"0000000000","omsId":"00000000-0000-4000-8000-000000000001"}""";

        var act = () => CrptSecurityGuard.ValidatePlainSettingsJson(json);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*must not be stored in crpt-settings.json*");
    }

    [Fact]
    public void CrptSecurityGuard_RejectsEmbeddedSecretValues()
    {
        const string secret = "00000000-0000-4000-8000-000000000099";
        var json = $$"""{"inn":"0000000000","suzBaseUrl":"https://suzgrid.crpt.ru/","note":"device {{secret}}"}""";
        var secrets = new CrptSecrets { ConnectionId = secret };

        var act = () => CrptSecurityGuard.ValidatePlainSettingsJson(json, secrets);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*secret value detected*");
    }

    [Fact]
    public void CrptSettingsStore_Save_RejectsPlaintextSecretsRegression()
    {
        var settings = new CrptSettings { Inn = "0000000000" };
        var secrets = new CrptSecrets
        {
            OmsId = "00000000-0000-4000-8000-000000000001",
            ConnectionId = "00000000-0000-4000-8000-000000000002",
            CertificateThumbprint = "ABCDEF1234567890",
            NkApiKey = "nk-secret-api-key",
        };

        _store.Save(settings, secrets);

        var plainJson = File.ReadAllText(_store.SettingsPath);
        plainJson.Should().NotContain(secrets.OmsId);
        plainJson.Should().NotContain(secrets.ConnectionId);
        plainJson.Should().NotContain(secrets.CertificateThumbprint);
        plainJson.Should().NotContain(secrets.NkApiKey);
        File.Exists(_store.SecretsPath).Should().BeTrue();
    }

    [Fact]
    public async Task GisMtClient_ErrorHandler_DoesNotEchoRawPayload()
    {
        var code = CrptTestFixtures.SyntheticMarkingCode(1);
        var handler = new UtilisationErrorHandler(code + " invalid payload");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://suz.test/") };
        using var client = new CrptGisMtClient(
            new CrptConnectionSettings { OmsId = "00000000-0000-4000-8000-000000000001", SuzBaseUrl = "https://suz.test/" },
            suzHttpClient: http,
            signOrderBody: (_, _) => "signed");

        var request = new UtilisationReportRequest
        {
            ProductGroup = "chemistry",
            RawPayloads = [code],
            ProductionDate = "2026-03-01",
            ExpirationDate = "2029-03-01",
        };

        var act = () => client.SendUtilisationAsync(
            CrptTestFixtures.SyntheticJwt(),
            CreateTestCertificate(),
            request,
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<CrptGisMtException>();
        ex.Which.Message.ToLowerInvariant().Should().NotContain("rawpayload");
        ex.Which.Message.Should().NotContain("SYNTHETICPAYLOAD");
        ex.Which.Message.Should().NotContain(((char)0x1D).ToString());
    }

    [Fact]
    public async Task SuzHttp_ErrorHandler_DoesNotEchoRawPayloadInException()
    {
        var code = CrptTestFixtures.SyntheticMarkingCode(3);
        var handler = new SuzPingErrorHandler(code);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://suz.test/") };
        using var client = new CrptSuzClient(
            new CrptConnectionSettings { OmsId = "00000000-0000-4000-8000-000000000001", SuzBaseUrl = "https://suz.test/" },
            http,
            disposeHttpClient: false);

        var act = () => client.PingAsync("synthetic-client-token", CancellationToken.None);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.ToLowerInvariant().Should().NotContain("rawpayload");
        ex.Which.Message.Should().NotContain("SYNTHETICPAYLOAD");
        ex.Which.Message.Should().NotContain(((char)0x1D).ToString());
    }

    [Fact]
    public void CrptAuthService_DoesNotLogTokenOnSuccess()
    {
        var authServiceSource = ReadRepoSource(
            "src", "DoubleMark.Desktop", "Services", "Crpt", "CrptAuthService.cs");

        authServiceSource.Should().NotContain("LoggingService", "auth success must not write tokens to logs");
    }

    [Fact]
    public void CrptSources_DoNotLogRawPayloadLiterals()
    {
        var violations = EnumerateCrptSourceFiles()
            .SelectMany(file => File.ReadAllLines(file)
                .Select((line, index) => (file, lineNumber: index + 1, line)))
            .Where(entry =>
                entry.line.Contains("LoggingService.", StringComparison.Ordinal)
                && entry.line.Contains("RawPayload", StringComparison.Ordinal))
            .Select(entry => $"{RelativeRepoPath(entry.file)}:{entry.lineNumber}: {entry.line.Trim()}")
            .ToList();

        violations.Should().BeEmpty("CRPT log calls must never reference RawPayload");
    }

    [Fact]
    public void CrptSources_DoNotUploadMarkingCodesToCloudServices()
    {
        var forbidden = new[]
        {
            "Supabase",
            "CloudScanHistory",
            "CloudScanHistoryService",
        };

        var violations = EnumerateCrptSourceFiles()
            .SelectMany(file => File.ReadAllLines(file)
                .Select((line, index) => (file, lineNumber: index + 1, line)))
            .Where(entry => forbidden.Any(token =>
                entry.line.Contains(token, StringComparison.Ordinal)))
            .Select(entry => $"{RelativeRepoPath(entry.file)}:{entry.lineNumber}: {entry.line.Trim()}")
            .ToList();

        violations.Should().BeEmpty(
            "CRPT code must not send marking payloads to cloud services (spec §12.1)");
    }

    [Fact]
    public void CrptSettingsView_ContainsUotDisclaimer()
    {
        var xaml = ReadRepoSource("src", "DoubleMark.Desktop", "Views", "CrptSettingsView.xaml");

        xaml.Should().Contain("УОТ");
        xaml.Should().Contain("ответствен");
    }

    private static IEnumerable<string> EnumerateCrptSourceFiles()
    {
        var repoRoot = FindRepoRoot();
        var roots = new[]
        {
            Path.Combine(repoRoot, "src", "DoubleMark.Crpt"),
            Path.Combine(repoRoot, "src", "DoubleMark.Desktop", "Services", "Crpt"),
            Path.Combine(repoRoot, "src", "DoubleMark.Desktop", "Settings"),
            Path.Combine(repoRoot, "src", "DoubleMark.Desktop", "ViewModels", "Crpt"),
        };

        foreach (var root in roots.Where(Directory.Exists))
        {
            var pattern = root.EndsWith("Settings", StringComparison.Ordinal) ? "Crpt*.cs" : "*.cs";
            foreach (var file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
                yield return file;
        }
    }

    private static string ReadRepoSource(params string[] segments)
    {
        var path = Path.Combine(FindRepoRoot(), Path.Combine(segments));
        File.Exists(path).Should().BeTrue(path);
        return File.ReadAllText(path);
    }

    private static string RelativeRepoPath(string absolutePath) =>
        Path.GetRelativePath(FindRepoRoot(), absolutePath).Replace('\\', '/');

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DoubleMark.sln")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static X509Certificate2 CreateTestCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=DoubleMark Test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private sealed class UtilisationErrorHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
    }

    private sealed class SuzPingErrorHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
    }
}
