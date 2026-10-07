using XenMotion.Application.Services;
using Xunit;

namespace XenMotion.Core.Tests.Services;

public class MediaFormatValidatorTests
{
    [Theory]
    [InlineData("video.mp4")]
    [InlineData("C:\\Media\\clip.m4v")]
    [InlineData("sample.webm")]
    [InlineData("animation.wmv")]
    [InlineData("archive.asf")]
    [InlineData("legacy.avi")]
    [InlineData("recording.mov")]
    public void IsSupportedFormat_ValidExtensions_ReturnsTrue(string path)
    {
        Assert.True(MediaFormatValidator.IsSupportedFormat(path));
    }

    [Theory]
    [InlineData("text.txt")]
    [InlineData("image.png")]
    [InlineData("app.exe")]
    [InlineData("audio.mp3")]
    [InlineData("document.pdf")]
    [InlineData("")]
    [InlineData("   ")]
    public void IsSupportedFormat_InvalidOrMissingExtensions_ReturnsFalse(string path)
    {
        Assert.False(MediaFormatValidator.IsSupportedFormat(path));
    }

    [Fact]
    public void Validate_NullOrEmpty_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentNullException>(() => MediaFormatValidator.Validate(null!));
        Assert.Throws<ArgumentException>(() => MediaFormatValidator.Validate(""));
        Assert.Throws<ArgumentException>(() => MediaFormatValidator.Validate("   "));
    }

    [Fact]
    public void Validate_UnsupportedExtension_ThrowsNotSupportedException()
    {
        var ex = Assert.Throws<NotSupportedException>(() => MediaFormatValidator.Validate("badfile.xyz"));
        Assert.Contains("not supported", ex.Message);
    }

    [Fact]
    public void Validate_SupportedFormatButMissingFile_ThrowsFileNotFoundException()
    {
        string missingPath = Path.Combine(Path.GetTempPath(), "non_existent_" + Guid.NewGuid().ToString("N") + ".mp4");
        var ex = Assert.Throws<FileNotFoundException>(() => MediaFormatValidator.Validate(missingPath));
        Assert.Equal(missingPath, ex.FileName);
    }

    [Fact]
    public void Validate_SupportedFormatAndExistingFile_Succeeds()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), "valid_" + Guid.NewGuid().ToString("N") + ".mp4");
        try
        {
            File.WriteAllText(tempFile, "dummy");
            // Should not throw
            MediaFormatValidator.Validate(tempFile);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
