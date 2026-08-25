using DoubleMark.Desktop.Services.Update;
using FluentAssertions;

namespace DoubleMark.Core.Tests;

public sealed class UpdateInstallerUrlTests
{
    [Theory]
    [InlineData("https://doublemark.ru/downloads/DoubleMarkSetup-2.1.6.exe")]
    [InlineData("https://www.doublemark.ru/downloads/DoubleMarkSetup-2.1.6.exe")]
    public void TryValidateInstallerUrl_allows_doublemark_https(string url)
    {
        UpdateService.TryValidateInstallerUrl(url, out var uri, out var error)
            .Should().BeTrue();
        error.Should().BeEmpty();
        uri.Host.Should().BeOneOf("doublemark.ru", "www.doublemark.ru");
    }

    [Theory]
    [InlineData("https://shamsyyy.github.io/doublemarksite/downloads/DoubleMarkSetup.exe")]
    [InlineData("https://github.com/Shamsyyy/dubliMark/releases/download/v2.1.6/DoubleMarkSetup.exe")]
    [InlineData("https://raw.githubusercontent.com/Shamsyyy/dubliMark/main/DoubleMarkSetup.exe")]
    [InlineData("http://doublemark.ru/downloads/DoubleMarkSetup.exe")]
    public void TryValidateInstallerUrl_rejects_github_and_non_https(string url)
    {
        UpdateService.TryValidateInstallerUrl(url, out _, out var error)
            .Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();
    }
}
