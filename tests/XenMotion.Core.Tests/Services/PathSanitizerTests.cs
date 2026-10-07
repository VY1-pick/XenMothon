using XenMotion.Application.Services;
using Xunit;

namespace XenMotion.Core.Tests.Services;

public class PathSanitizerTests
{
    [Fact]
    public void SanitizeFileName_NormalFileName_ReturnsUnchanged()
    {
        string safe = PathSanitizer.SanitizeFileName("wallpaper.mp4");
        Assert.Equal("wallpaper.mp4", safe);
    }

    [Theory]
    [InlineData("../../etc/passwd.mp4", "passwd.mp4")]
    [InlineData("..\\..\\windows\\system32\\evil.png", "evil.png")]
    [InlineData("folder/sub/nature.jpg", "nature.jpg")]
    [InlineData("C:\\User\\Desktop\\photo.webp", "photo.webp")]
    public void SanitizeFileName_DirectoryTraversal_StripsPathComponents(string raw, string expected)
    {
        string safe = PathSanitizer.SanitizeFileName(raw);
        Assert.Equal(expected, safe);
    }

    [Fact]
    public void SanitizeFileName_InvalidCharacters_ReplacesWithUnderscore()
    {
        string safe = PathSanitizer.SanitizeFileName("bad<name>:test*file?.mp4");
        Assert.DoesNotContain("<", safe);
        Assert.DoesNotContain(">", safe);
        Assert.DoesNotContain(":", safe);
        Assert.DoesNotContain("*", safe);
        Assert.DoesNotContain("?", safe);
        Assert.EndsWith(".mp4", safe);
    }

    [Theory]
    [InlineData("CON.mp4", "wp_CON.mp4")]
    [InlineData("prn.png", "wp_prn.png")]
    [InlineData("AUX.avi", "wp_AUX.avi")]
    [InlineData("NUL.webm", "wp_NUL.webm")]
    [InlineData("com1.jpg", "wp_com1.jpg")]
    [InlineData("lpt1.bmp", "wp_lpt1.bmp")]
    public void SanitizeFileName_WindowsReservedDeviceNames_PrefixesSafeTag(string raw, string expected)
    {
        string safe = PathSanitizer.SanitizeFileName(raw);
        Assert.Equal(expected, safe, ignoreCase: true);
    }

    [Fact]
    public void SanitizeFileName_NullOrWhitespace_GeneratesSafeUniqueName()
    {
        string safe1 = PathSanitizer.SanitizeFileName(null);
        string safe2 = PathSanitizer.SanitizeFileName("   ");

        Assert.StartsWith("wallpaper_", safe1);
        Assert.StartsWith("wallpaper_", safe2);
        Assert.NotEqual(safe1, safe2);
    }

    [Fact]
    public void IsPathWithinRoot_ValidChild_ReturnsTrue()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "XenMotionRoot"));
        string child = Path.Combine(root, "Media", "123", "test.mp4");

        Assert.True(PathSanitizer.IsPathWithinRoot(child, root));
    }

    [Fact]
    public void IsPathWithinRoot_EscapingPath_ReturnsFalse()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "XenMotionRoot"));
        string escaped = Path.Combine(root, "..", "outside.mp4");

        Assert.False(PathSanitizer.IsPathWithinRoot(escaped, root));
    }
}
