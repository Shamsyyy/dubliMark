using DoubleMark.Desktop.Services.Update;
using FluentAssertions;

namespace DoubleMark.Core.Tests;

public class UpdateVersionComparerTests
{
    [Theory]
    [InlineData("2.1.1", "2.1.0", true)]
    [InlineData("2.1.10", "2.1.9", true)]
    [InlineData("2.1.0", "2.1.0", false)]
    [InlineData("2.0.9", "2.1.0", false)]
    public void IsNewer_compares_semantic_versions(string remote, string current, bool expected) =>
        VersionComparer.IsNewer(remote, current).Should().Be(expected);

    [Theory]
    [InlineData("2.0.0", "2.1.0", true)]
    [InlineData("2.1.0", "2.1.0", false)]
    [InlineData("2.2.0", "2.1.0", false)]
    public void IsBelowMinimum_detects_unsupported_versions(string current, string minimum, bool expected) =>
        VersionComparer.IsBelowMinimum(current, minimum).Should().Be(expected);
}
