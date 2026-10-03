namespace Lumen.Core.Models;

/// <summary>
/// Progress notification during single or batch wallpaper import.
/// </summary>
public sealed record ImportProgress(int CompletedCount, int TotalCount, string CurrentFileName);

/// <summary>
/// Result of an individual wallpaper file import operation.
/// </summary>
public sealed record ImportResult
{
    public required string SourceFilePath { get; init; }
    public bool Success { get; init; }
    public bool IsDuplicate { get; init; }
    public Wallpaper? ImportedWallpaper { get; init; }
    public Wallpaper? ExistingWallpaper { get; init; }
    public string? ErrorMessage { get; init; }

    public static ImportResult Succeeded(string sourceFilePath, Wallpaper wallpaper) =>
        new()
        {
            SourceFilePath = sourceFilePath,
            Success = true,
            ImportedWallpaper = wallpaper
        };

    public static ImportResult Duplicate(string sourceFilePath, Wallpaper existingWallpaper) =>
        new()
        {
            SourceFilePath = sourceFilePath,
            Success = true,
            IsDuplicate = true,
            ExistingWallpaper = existingWallpaper
        };

    public static ImportResult Failed(string sourceFilePath, string errorMessage) =>
        new()
        {
            SourceFilePath = sourceFilePath,
            Success = false,
            ErrorMessage = errorMessage
        };
}
