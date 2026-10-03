namespace Lumen.Core.Models;

/// <summary>
/// Result of a wallpaper deletion operation across database and filesystem.
/// </summary>
public sealed record DeleteResult
{
    public required Guid WallpaperId { get; init; }
    public bool Success { get; init; }
    public bool DatabaseDeleted { get; init; }
    public bool MediaDeleted { get; init; }
    public bool ThumbnailDeleted { get; init; }
    public string? ErrorMessage { get; init; }

    public static DeleteResult Succeeded(Guid id) =>
        new()
        {
            WallpaperId = id,
            Success = true,
            DatabaseDeleted = true,
            MediaDeleted = true,
            ThumbnailDeleted = true
        };

    public static DeleteResult NotFound(Guid id) =>
        new()
        {
            WallpaperId = id,
            Success = false,
            ErrorMessage = "Wallpaper not found in library."
        };

    public static DeleteResult PartialSuccess(Guid id, string message) =>
        new()
        {
            WallpaperId = id,
            Success = true,
            DatabaseDeleted = true,
            ErrorMessage = message
        };

    public static DeleteResult Failed(Guid id, string error) =>
        new()
        {
            WallpaperId = id,
            Success = false,
            ErrorMessage = error
        };
}
