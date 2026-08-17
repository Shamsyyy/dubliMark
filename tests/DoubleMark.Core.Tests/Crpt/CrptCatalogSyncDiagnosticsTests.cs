using DoubleMark.Core.Crpt;
using DoubleMark.Crpt;
using DoubleMark.Desktop.Services.Crpt;
using DoubleMark.Desktop.Settings;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

public class CrptCatalogSyncDiagnosticsTests
{
    [Fact]
    public void RedactHost_TruncatesLongPunycodeHost()
    {
        var redacted = CrptCatalogSyncService.RedactHost(CrptUrl.ProductionNkBaseUrl);
        redacted.Should().StartWith("xn--80");
        redacted.Should().Contain("…");
        redacted.Should().NotBe(CrptUrl.ProductionNkPunycodeHost);
    }

    [Fact]
    public void WrapSyncFailure_PreservesNkConnectionMessage()
    {
        var inner = new InvalidOperationException(
            "Порт 443 к production NK недоступен. Sandbox: https://api.nk.sandbox.crptech.ru/");
        var settings = new CrptSettings { NkUseJwtFromTrueApi = true };

        var wrapped = CrptCatalogSyncService.WrapSyncFailure(inner, "product-list", "api.xn…p1ai", settings);

        wrapped.Should().BeSameAs(inner);
    }

    [Fact]
    public void WrapSyncFailure_AddsStageAndAuthHint()
    {
        var inner = new Exception("generic failure");
        var settings = new CrptSettings { NkUseJwtFromTrueApi = false };

        var wrapped = CrptCatalogSyncService.WrapSyncFailure(inner, "feed-product", "sandbox", settings);

        wrapped.Message.Should().Contain("feed-product");
        wrapped.Message.Should().Contain("API KEY");
    }

    [Fact]
    public void BuildEmptyProductListException_MentionsContourAndSave()
    {
        var settings = new CrptSettings { Environment = CrptEnvironment.Production, NkUseJwtFromTrueApi = true };
        var ex = CrptCatalogSyncService.BuildEmptyProductListException(settings, "xn--80…p1ai");

        ex.Message.Should().Contain("Production");
        ex.Message.Should().Contain("Сохранить");
        ex.Message.Should().Contain("ИНН");
    }

}
