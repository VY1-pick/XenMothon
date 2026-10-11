using XenMotion.Core.Enums;

namespace XenMotion.Core.Interfaces;

/// <summary>Platform image decoding and video-frame extraction for the persistent thumbnail cache.</summary>
public interface IThumbnailCodec
{
    bool IsValid(string path);
    Task GenerateAsync(string source, string destination, WallpaperType type, CancellationToken ct);
}
