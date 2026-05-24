using DoubleMark.Desktop.Services.Update;
using Xunit;

namespace DoubleMark.Core.Tests;

public sealed class PublisherNameMatcherTests
{
    [Theory]
    [InlineData("CN=DoubleMark, O=DoubleMark, C=RU", true)]
    [InlineData("CN=Other Vendor, O=Other, C=US", false)]
    public void IsAllowed_matches_expected_publishers(string subject, bool expected)
    {
        var allowed = AuthenticodeSignatureVerifier.DefaultAllowedPublisherMarkers;
        Assert.Equal(expected, PublisherNameMatcher.IsAllowed(subject, allowed));
    }

    [Fact]
    public void ExtractDisplayName_reads_cn()
    {
        var name = PublisherNameMatcher.ExtractDisplayName("CN=DoubleMark, O=DoubleMark, C=RU");
        Assert.Equal("DoubleMark", name);
    }

    [Fact]
    public void VerifyInstaller_skips_signature_when_not_required()
    {
        var path = Path.Combine(Path.GetTempPath(), "doublemark-unsigned-test-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllBytes(path, [0x4D, 0x5A]);
        try
        {
            var result = AuthenticodeSignatureVerifier.VerifyInstaller(path, requireSignature: false);
            Assert.True(result.IsValid);
            Assert.Equal("unsigned-allowed", result.PublisherName);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
