using System.Net;
using System.Net.Http.Headers;
using DoubleMark.Core.Crpt;
using DoubleMark.Crpt;
using DoubleMark.Desktop.Settings;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

/// <summary>
/// §15 — Risk mitigations and open questions tracked in code.
/// </summary>
public class CrptRiskMitigationTests
{
    public static IEnumerable<object[]> Section15RiskRows =>
        CrptRiskMitigations.AllRisks.Select(risk => new object[] { risk });

    [Theory]
    [MemberData(nameof(Section15RiskRows))]
    public void Section15_EachRiskRow_HasMitigationCodePath(CrptRiskKind risk)
    {
        var description = CrptRiskMitigations.DescribeMitigation(risk);
        description.Should().NotBeNullOrWhiteSpace();

        switch (risk)
        {
            case CrptRiskKind.CryptoProWindowsOnly:
                typeof(CrptPlatformGuard).Should().NotBeNull();
                CrptRiskMitigations.CryptoProWindowsDependencyNote.Should().Contain("Windows");
                break;
            case CrptRiskKind.SuzUrlFromSettingsOnly:
                CrptRiskMitigations.ResolveSuzBaseUrl("https://suz2.crpt.ru/")
                    .Should().Be("https://suz2.crpt.ru/");
                break;
            case CrptRiskKind.AttributesPerProductGroup:
                CrptRiskMitigations.AttributesPerProductGroupNote.Should().Contain("Appendix");
                break;
            case CrptRiskKind.ConnectionIdExpiry:
                CrptRiskMitigations.ConnectionIdExpiredUserMessage.Should().Contain("Connection ID");
                break;
            case CrptRiskKind.TokenTenHourExpiry:
                typeof(CrptApiException).GetProperty(nameof(CrptApiException.IsTokenExpired))
                    .Should().NotBeNull();
                break;
            case CrptRiskKind.NkApiRateLimit:
                CrptNkClient.RateLimitBackoffPlaceholder.Should().Be(CrptRiskMitigations.NkRateLimitInitialBackoff);
                CrptRiskMitigations.ApiUsageLimitHeaderName.Should().Be("API-Usage-Limit");
                break;
            case CrptRiskKind.LargeCatalogPhase2:
                CrptRiskMitigations.LargeCatalogMitigationNote.Should().Contain("10 000");
                break;
            case CrptRiskKind.TemplateIdDefaults:
                CrptRiskMitigations.ResolveTemplateId(CrptProductGroup.Chemistry)
                    .Should().Be(46);
                break;
            case CrptRiskKind.UotLegalDisclaimer:
                CrptRiskMitigations.UotLegalDisclaimer.Should().Contain("УОТ");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(risk), risk, null);
        }
    }

    [Fact]
    public void Section15_AllNineRiskRows_AreTracked()
    {
        CrptRiskMitigations.AllRisks.Should().HaveCount(9);
    }

    [Fact]
    public void OpenQuestions_BeforePhaseB0_AreUnresolved()
    {
        CrptOpenQuestionsTracker.BeforePhaseB0.Should().HaveCount(3);

        foreach (var question in CrptOpenQuestionsTracker.BeforePhaseB0)
        {
            CrptOpenQuestionsTracker.IsUnresolved(question).Should().BeTrue(
                because: CrptOpenQuestionsTracker.Describe(question));
            CrptOpenQuestionsTracker.GetPhase(question).Should().Be(CrptOpenQuestionPhase.BeforePhaseB0);
        }
    }

    [Fact]
    public void OpenQuestions_BeforePhaseB_AreUnresolved()
    {
        CrptOpenQuestionsTracker.BeforePhaseB.Should().HaveCount(4);

        foreach (var question in CrptOpenQuestionsTracker.BeforePhaseB)
        {
            CrptOpenQuestionsTracker.IsUnresolved(question).Should().BeTrue(
                because: CrptOpenQuestionsTracker.Describe(question));
            CrptOpenQuestionsTracker.GetPhase(question).Should().Be(CrptOpenQuestionPhase.BeforePhaseB);
        }
    }

    [Fact]
    public void OpenQuestions_AllSevenItems_AreTracked()
    {
        CrptOpenQuestionsTracker.All.Should().HaveCount(7);
        CrptOpenQuestionsTracker.All.Should().OnlyContain(q => CrptOpenQuestionsTracker.IsUnresolved(q));
    }

    [Fact]
    public void PlatformGuard_IsWindows_MatchesRuntime()
    {
        CrptPlatformGuard.IsWindows.Should().Be(OperatingSystem.IsWindows());
    }

    [Fact]
    public void PlatformGuard_OnWindows_DoesNotThrowForCertOps()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var act = () => CrptPlatformGuard.EnsureWindowsForCertificateOperations();
        act.Should().NotThrow();
    }

    [Fact]
    public void SuzUrlMitigation_RejectsEmptyConfiguredUrl()
    {
        var act = () => CrptRiskMitigations.ResolveSuzBaseUrl("  ");
        act.Should().Throw<ArgumentException>()
            .WithMessage("*SUZ URL*");
    }

    [Fact]
    public void ConnectionSettingsBridge_UsesSuzUrlFromSettingsOnly()
    {
        var settings = new CrptSettings { SuzBaseUrl = "https://suz2.crpt.ru/" };
        var connection = CrptConnectionSettingsBridge.ToConnectionSettings(settings, new CrptSecrets());

        connection.SuzBaseUrl.Should().Be("https://suz2.crpt.ru/");
    }

    [Fact]
    public void CrptApiException_IsTokenExpired_When401()
    {
        var ex = CrptApiException.FromHttpResponse(HttpStatusCode.Unauthorized, "{}");

        ex.IsTokenExpired.Should().BeTrue();
        ex.Message.Should().Contain(CrptRiskMitigations.TokenExpiredUserMessage);
    }

    [Fact]
    public void CrptApiException_IsRateLimited_When429()
    {
        var ex = CrptApiException.FromHttpResponse(HttpStatusCode.TooManyRequests, "{}", "50/1000");

        ex.IsRateLimited.Should().BeTrue();
        ex.ApiUsageLimit.Should().Be("50/1000");
    }

    [Fact]
    public void ConnectionIdExpiry_DetectedFromAuthResponse()
    {
        CrptRiskMitigations.LooksLikeConnectionIdExpiry(
                403,
                """{"error":"connectionId expired"}""")
            .Should().BeTrue();

        CrptRiskMitigations.FormatConnectionIdExpiryMessage(
                403,
                """{"error":"connectionId expired"}""")
            .Should().Be(CrptRiskMitigations.ConnectionIdExpiredUserMessage);
    }

    [Fact]
    public async Task NkClient_429_CapturesApiUsageLimitHeader()
    {
        var handler = new NkRateLimitHandler("75/1000");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://nk.test/") };
        using var client = new CrptNkClient(
            new CrptConnectionSettings { NkBaseUrl = "https://nk.test/" },
            bearerToken: "synthetic-token",
            httpClient: http);

        var act = () => client.GetProductListAsync();

        var ex = await act.Should().ThrowAsync<CrptApiException>();
        ex.Which.IsRateLimited.Should().BeTrue();
        ex.Which.ApiUsageLimit.Should().Be("75/1000");
        client.LastApiUsageLimit.Should().Be("75/1000");
        ex.Which.Message.Should().Contain("retry after");
    }

    [Fact]
    public void CrptSettings_ResolveTemplateId_UsesRiskMitigationFallback()
    {
        var settings = new CrptSettings();
        settings.ResolveTemplateId(CrptProductGroup.Chemistry).Should().Be(46);

        settings.ProductGroupTemplateDefaults[CrptProductGroup.Milk] = 99;
        settings.ResolveTemplateId(CrptProductGroup.Milk).Should().Be(99);
    }

    private sealed class NkRateLimitHandler(string usageLimit) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("""{"error":"rate limit"}"""),
            };
            response.Headers.Add(CrptRiskMitigations.ApiUsageLimitHeaderName, usageLimit);
            return Task.FromResult(response);
        }
    }
}
