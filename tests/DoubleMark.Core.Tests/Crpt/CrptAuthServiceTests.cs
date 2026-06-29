using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DoubleMark.Crpt;
using DoubleMark.Desktop.Services.Crpt;
using DoubleMark.Desktop.Settings;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

public class CrptAuthServiceTests : IDisposable
{
    private const string TestInn = "0000000000";
    private const string AuthKeyPath = "api/v3/true-api/auth/key";
    private const string SignInPath = "api/v3/true-api/auth/simpleSignIn";

    private readonly string _tempDirectory;
    private readonly CrptSettingsStore _settingsStore;
    private readonly CrptAuthRuntimeState _runtimeState;
    private readonly X509Certificate2 _certificate;
    private int _authKeyCalls;
    private int _signInCalls;
    private int _concurrentSignInCalls;
    private CrptAuthTestHandler? _lastHandler;

    public CrptAuthServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "DoubleMark.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _settingsStore = new CrptSettingsStore(_tempDirectory);
        _runtimeState = new CrptAuthRuntimeState();
        _certificate = CreateTestCertificate();

        _settingsStore.Save(new CrptSettings
        {
            Inn = TestInn,
            TrueApiBaseUrl = "https://markirovka.crpt.ru/",
        }, new CrptSecrets());
    }

    public void Dispose()
    {
        _certificate.Dispose();
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
    public async Task GetValidTokenAsync_ReturnsCachedTokenWhenStillValid()
    {
        var expireDate = DateTimeOffset.UtcNow.AddHours(10);
        var service = CreateService(
            """{"uuid":"00000000-0000-4000-8000-000000000001","data":"dGVzdC1wYXlsb2Fk"}""",
            $$"""{"token":"cached-token","expireDate":"{{expireDate:O}}"}""");

        var first = await service.GetValidTokenAsync();
        var second = await service.GetValidTokenAsync();

        first.Should().Be("cached-token");
        second.Should().Be("cached-token");
        _authKeyCalls.Should().Be(1);
        _signInCalls.Should().Be(1);
        service.TokenExpiresAt.Should().Be(expireDate);
        _runtimeState.TokenExpiresAt.Should().Be(expireDate);
    }

    [Fact]
    public async Task GetValidTokenAsync_RefreshesWhenWithinFifteenMinutesOfExpiry()
    {
        var expireDate = DateTimeOffset.UtcNow.AddHours(10);
        var service = CreateService(
            """{"uuid":"00000000-0000-4000-8000-000000000002","data":"dGVzdC1wYXlsb2Fk"}""",
            $$"""{"token":"fresh-token","expireDate":"{{expireDate:O}}"}""");

        SeedCachedToken(service, "stale-token", DateTimeOffset.UtcNow.AddMinutes(10));

        var token = await service.GetValidTokenAsync();

        token.Should().Be("fresh-token");
        _authKeyCalls.Should().Be(1);
        _signInCalls.Should().Be(1);
    }

    [Fact]
    public async Task GetValidTokenAsync_ConcurrentRefreshUsesSingleHttpRound()
    {
        var expireDate = DateTimeOffset.UtcNow.AddHours(10);
        var service = CreateService(
            """{"uuid":"00000000-0000-4000-8000-000000000003","data":"dGVzdC1wYXlsb2Fk"}""",
            $$"""{"token":"concurrent-token","expireDate":"{{expireDate:O}}"}""");

        _lastHandler!.SignInDelay = TimeSpan.FromMilliseconds(150);

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => service.GetValidTokenAsync())
            .ToArray();

        var tokens = await Task.WhenAll(tasks);

        tokens.Should().OnlyContain(t => t == "concurrent-token");
        _authKeyCalls.Should().Be(1);
        _signInCalls.Should().Be(1);
        _concurrentSignInCalls.Should().BeLessOrEqualTo(1);
    }

    [Fact]
    public async Task RefreshTokenAsync_ForcesRefreshEvenWhenCacheValid()
    {
        var firstExpiry = DateTimeOffset.UtcNow.AddHours(10);
        var secondExpiry = DateTimeOffset.UtcNow.AddHours(11);
        var service = CreateService(
            """{"uuid":"00000000-0000-4000-8000-000000000004","data":"dGVzdC1wYXlsb2Fk"}""",
            $$"""{"token":"first-token","expireDate":"{{firstExpiry:O}}"}""");

        var first = await service.GetValidTokenAsync();
        first.Should().Be("first-token");
        _authKeyCalls.Should().Be(1);
        _signInCalls.Should().Be(1);

        _lastHandler!.Responses[SignInPath] = (HttpStatusCode.OK,
            $$"""{"token":"forced-token","expireDate":"{{secondExpiry:O}}"}""");

        await service.RefreshTokenAsync();
        var forced = await service.GetValidTokenAsync();

        forced.Should().Be("forced-token");
        _authKeyCalls.Should().Be(2);
        _signInCalls.Should().Be(2);
        service.TokenExpiresAt.Should().Be(secondExpiry);
    }

    [Fact]
    public async Task AuthenticateJwtAsync_SendsInnWithUnitedTokenFalse()
    {
        var expireDate = DateTimeOffset.UtcNow.AddHours(10);
        using var client = CreateAuthClient(
            """{"uuid":"00000000-0000-4000-8000-000000000005","data":"dGVzdC1wYXlsb2Fk"}""",
            $$"""{"token":"flow-token","expireDate":"{{expireDate:O}}"}""");

        var token = await client.AuthenticateJwtAsync(_certificate, CancellationToken.None);

        token.Value.Should().Be("flow-token");
        token.ExpiresAt.Should().Be(expireDate);
        token.IsUnitedUuidToken.Should().BeFalse();
        _lastHandler!.RequestBodies.Should().ContainSingle(body =>
            body.Contains("\"unitedToken\":false", StringComparison.Ordinal)
            && body.Contains($"\"inn\":\"{TestInn}\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HostedService_WhenAutoRefreshEnabled_RefreshesToken()
    {
        var expireDate = DateTimeOffset.UtcNow.AddHours(10);
        var authService = CreateService(
            """{"uuid":"00000000-0000-4000-8000-000000000006","data":"dGVzdC1wYXlsb2Fk"}""",
            $$"""{"token":"hosted-token","expireDate":"{{expireDate:O}}"}""");

        var hostedService = new FastRefreshHostedService(authService, _settingsStore);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await hostedService.StartAsync(cts.Token);
        await Task.Delay(100, CancellationToken.None);
        await hostedService.StopAsync(CancellationToken.None);

        _authKeyCalls.Should().BeGreaterOrEqualTo(1);
        _signInCalls.Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public async Task HostedService_WhenAutoRefreshDisabled_SkipsRefresh()
    {
        _settingsStore.Save(new CrptSettings
        {
            Inn = TestInn,
            AutoRefreshToken = false,
        }, new CrptSecrets());

        var countingAuth = new CountingAuthService();
        var hostedService = new FastRefreshHostedService(countingAuth, _settingsStore);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await hostedService.StartAsync(cts.Token);
        await Task.Delay(100, CancellationToken.None);
        await hostedService.StopAsync(CancellationToken.None);

        countingAuth.CallCount.Should().Be(0);
    }

    private CrptAuthService CreateService(string keyJson, string signInJson)
    {
        _authKeyCalls = 0;
        _signInCalls = 0;
        _concurrentSignInCalls = 0;

        var handler = CreateHandler();
        handler.Responses[AuthKeyPath] = (HttpStatusCode.OK, keyJson);
        handler.Responses[SignInPath] = (HttpStatusCode.OK, signInJson);
        _lastHandler = handler;

        return new CrptAuthService(
            _settingsStore,
            new TestCertificateProvider(_certificate),
            _runtimeState,
            connection => CreateAuthClient(connection, handler));
    }

    private CrptAuthClient CreateAuthClient(string keyJson, string signInJson)
    {
        var handler = CreateHandler();
        handler.Responses[AuthKeyPath] = (HttpStatusCode.OK, keyJson);
        handler.Responses[SignInPath] = (HttpStatusCode.OK, signInJson);
        _lastHandler = handler;

        var connection = CrptConnectionSettingsBridge.ToConnectionSettings(
            _settingsStore.LoadSettings(),
            _settingsStore.LoadSecrets());
        return CreateAuthClient(connection, handler);
    }

    private CrptAuthClient CreateAuthClient(
        CrptConnectionSettings connection,
        CrptAuthTestHandler handler)
    {
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://markirovka.crpt.ru/")
        };

        return new CrptAuthClient(
            connection,
            http,
            disposeHttpClient: false,
            signAuthKeyData: (_, _) => "signed-test-payload");
    }

    private CrptAuthTestHandler CreateHandler() =>
        new(
            onAuthKey: () => Interlocked.Increment(ref _authKeyCalls),
            onSignIn: () =>
            {
                Interlocked.Increment(ref _signInCalls);
                Interlocked.Increment(ref _concurrentSignInCalls);
            },
            onSignInComplete: () => Interlocked.Decrement(ref _concurrentSignInCalls));

    private static void SeedCachedToken(CrptAuthService service, string value, DateTimeOffset expiresAt)
    {
        var token = new CrptAuthToken(value, expiresAt, IsUnitedUuidToken: true);
        typeof(CrptAuthService)
            .GetField("_cached", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(service, token);
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

    private sealed class TestCertificateProvider(X509Certificate2 certificate) : ICrptCertificateProvider
    {
        public X509Certificate2 FindCertificate(CrptConnectionSettings settings) => certificate;

        public IReadOnlyList<CrptCertificateDescriptor> ListEligibleCertificates(string? innFilter = null) =>
            [new CrptCertificateDescriptor(certificate.Subject, certificate.Thumbprint, certificate.NotAfter)];
    }

    private sealed class CountingAuthService : ICrptAuthService
    {
        public int CallCount { get; private set; }
        public DateTimeOffset? TokenExpiresAt => null;

        public Task<string> GetValidTokenAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult("counted-token");
        }

        public Task RefreshTokenAsync(CancellationToken cancellationToken = default) =>
            GetValidTokenAsync(cancellationToken);
    }

    private sealed class FastRefreshHostedService : CrptTokenRefreshHostedService
    {
        public FastRefreshHostedService(ICrptAuthService authService, ICrptSettingsStore settingsStore)
            : base(authService, settingsStore)
        {
        }

        protected override TimeSpan RefreshInterval => TimeSpan.FromMilliseconds(50);
    }

    private sealed class CrptAuthTestHandler(
        Action onAuthKey,
        Action onSignIn,
        Action onSignInComplete) : HttpMessageHandler
    {
        public Dictionary<string, (HttpStatusCode StatusCode, string Body)> Responses { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public List<string> RequestBodies { get; } = [];
        public TimeSpan SignInDelay { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            if (request.Method == HttpMethod.Post && request.Content is not null)
                RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));

            if (path.Equals(AuthKeyPath, StringComparison.OrdinalIgnoreCase))
            {
                onAuthKey();
                return CreateResponse(AuthKeyPath);
            }

            if (path.Equals(SignInPath, StringComparison.OrdinalIgnoreCase))
            {
                onSignIn();
                if (SignInDelay > TimeSpan.Zero)
                    await Task.Delay(SignInDelay, cancellationToken);

                try
                {
                    return CreateResponse(SignInPath);
                }
                finally
                {
                    onSignInComplete();
                }
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"Unexpected path: {path}")
            };
        }

        private HttpResponseMessage CreateResponse(string path)
        {
            var (statusCode, body) = Responses[path];
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }
}
