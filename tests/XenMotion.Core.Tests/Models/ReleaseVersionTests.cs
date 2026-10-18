using XenMotion.Core.Models.Updates;
using Xunit;

namespace XenMotion.Core.Tests.Models;

public class ReleaseVersionTests
{
    [Theory]
    [InlineData("0.9.0-beta.4", "0.9.0-beta.3", 1)]
    [InlineData("0.9.0-beta.3", "0.9.0-beta.4", -1)]
    [InlineData("0.9.0-beta.3", "0.9.0-beta.3", 0)]
    [InlineData("0.9.0", "0.9.0-beta.9", 1)]
    [InlineData("0.9.0-beta.9", "0.9.0", -1)]
    [InlineData("1.0.0", "0.9.9", 1)]
    [InlineData("v0.9.0-beta.4", "0.9.0-beta.3", 1)]
    [InlineData("0.9.0-rc.1", "0.9.0-beta.9", 1)]
    [InlineData("0.9.0-beta.4+build.77", "0.9.0-beta.4", 0)]
    [InlineData("0.9.1", "0.9.0-beta.4", 1)]
    public void VersionsOrderNumericallyThenByPreReleaseLabel(string left, string right, int expected)
    {
        Assert.Equal(expected, Math.Sign(ReleaseVersion.Compare(left, right)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("beta")]
    [InlineData("1.2.3.4.5")]
    [InlineData("1.x.0")]
    [InlineData("1.0.0-")]
    public void InvalidTextIsRejected(string text)
        => Assert.False(ReleaseVersion.TryParse(text, out _));

    [Fact]
    public void ParsedComponentsSurviveARoundTrip()
    {
        Assert.True(ReleaseVersion.TryParse("0.9.0-beta.3", out var version));
        Assert.Equal(0, version!.Major);
        Assert.Equal(9, version.Minor);
        Assert.Equal(0, version.Patch);
        Assert.True(version.IsPreRelease);
        Assert.Equal(["beta", "3"], version.PreRelease);
        Assert.Equal("0.9.0-beta.3", version.ToString());
    }

    [Fact]
    public void UnparseableTextFallsBackToOrdinalComparison()
        => Assert.Equal(0, ReleaseVersion.Compare("nightly", "nightly"));
}
