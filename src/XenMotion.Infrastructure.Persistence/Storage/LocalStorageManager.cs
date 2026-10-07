using System.Security.Cryptography;
using XenMotion.Application.Services;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models;
using Microsoft.Extensions.Logging;

namespace XenMotion.Infrastructure.Persistence.Storage;

/// <summary>
/// Manages physical storage of media files on disk, ensuring atomic copy, SHA-256 hashing,
/// directory traversal protection, storage-relative path resolution, and safe root migrations.
/// </summary>
public sealed class LocalStorageManager : IStorageManager
{
    private string _rootStoragePath;
    private readonly ILogger<LocalStorageManager> _logger;
    private readonly object _syncLock = new();

    public string RootStoragePath
    {
        get
        {
            lock (_syncLock)
            {
                return _rootStoragePath;
            }
        }
    }

    public string MediaDirectory => Path.Combine(RootStoragePath, "Media");
    public string ThumbnailsDirectory => Path.Combine(RootStoragePath, "Thumbnails");
    public string TempDirectory => Path.Combine(RootStoragePath, "Temp");

    public LocalStorageManager(string rootStoragePath, ILogger<LocalStorageManager> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootStoragePath);
        _rootStoragePath = Path.GetFullPath(rootStoragePath);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        EnsureBaseDirectories();
    }

    public string GetWallpaperDirectory(Guid wallpaperId)
    {
        return Path.Combine(MediaDirectory, wallpaperId.ToString());
    }

    public string ResolveAbsolutePath(string relativeOrAbsolutePath)
    {
        if (string.IsNullOrWhiteSpace(relativeOrAbsolutePath))
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(relativeOrAbsolutePath))
        {
            return Path.GetFullPath(relativeOrAbsolutePath);
        }

        return Path.GetFullPath(Path.Combine(RootStoragePath, relativeOrAbsolutePath));
    }

    public string MakeStorageRelativePath(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return string.Empty;
        }

        string normalizedRoot = RootStoragePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string normalizedPath = Path.GetFullPath(fullPath);

        if (normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedPath[normalizedRoot.Length..].Replace('\\', '/');
        }

        return fullPath;
    }

    public async Task<string> StageImportAsync(string sourceFilePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);

        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException("Import source file not found.", sourceFilePath);
        }

        string safeName = PathSanitizer.SanitizeFileName(Path.GetFileName(sourceFilePath));
        string tempFileName = $"{Guid.NewGuid():N}_{safeName}";
        string stagedPath = Path.Combine(TempDirectory, tempFileName);

        _logger.LogInformation("Staging file '{Source}' to '{Destination}'.", sourceFilePath, stagedPath);

        const int bufferSize = 81920; // 80KB streaming buffer
        await using var sourceStream = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
        await using var targetStream = new FileStream(stagedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize, useAsync: true);

        await sourceStream.CopyToAsync(targetStream, bufferSize, ct).ConfigureAwait(false);
        return stagedPath;
    }

    public async Task<string> CommitImportAsync(
        string stagedFilePath,
        Guid wallpaperId,
        string fileName,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagedFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        if (!File.Exists(stagedFilePath))
        {
            throw new FileNotFoundException("Staged file not found for commitment.", stagedFilePath);
        }

        string sanitizedFileName = PathSanitizer.SanitizeFileName(fileName);
        string targetDir = GetWallpaperDirectory(wallpaperId);

        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        string finalPath = Path.Combine(targetDir, sanitizedFileName);

        // Verify destination stays within managed root boundary
        if (!PathSanitizer.IsPathWithinRoot(finalPath, RootStoragePath))
        {
            throw new UnauthorizedAccessException("Destination path escapes the root storage directory.");
        }

        // Move into final storage
        File.Move(stagedFilePath, finalPath, overwrite: true);
        _logger.LogInformation("Committed staged file to final path '{FinalPath}'.", finalPath);

        return await Task.FromResult(finalPath).ConfigureAwait(false);
    }

    public Task<bool> DeleteWallpaperStorageAsync(Guid wallpaperId, CancellationToken ct = default)
    {
        string dir = GetWallpaperDirectory(wallpaperId);
        if (Directory.Exists(dir))
        {
            try
            {
                Directory.Delete(dir, recursive: true);
                _logger.LogInformation("Deleted storage directory for wallpaper '{Id}'.", wallpaperId);
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete storage directory for wallpaper '{Id}'.", wallpaperId);
                return Task.FromResult(false);
            }
        }

        return Task.FromResult(true);
    }

    public Task<bool> ValidateStoragePathAsync(string targetPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return Task.FromResult(false);
        }

        try
        {
            string fullPath = Path.GetFullPath(targetPath);
            return Task.FromResult(PathSanitizer.IsPathWithinRoot(fullPath, RootStoragePath));
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public async Task<string> CalculateSha256HashAsync(string filePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Cannot compute hash. File not found.", filePath);
        }

        const int bufferSize = 81920; // 80KB chunked read
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
        using var sha256 = SHA256.Create();

        byte[] hashBytes = await sha256.ComputeHashAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public Task<StorageUsageInfo> GetStorageUsageAsync(int wallpaperCount = 0, CancellationToken ct = default)
    {
        long mediaSize = CalculateDirectorySize(MediaDirectory);
        long thumbnailSize = CalculateDirectorySize(ThumbnailsDirectory);
        long totalSize = mediaSize + thumbnailSize;

        long freeSpace = 0;
        try
        {
            string root = Path.GetPathRoot(RootStoragePath) ?? RootStoragePath;
            var drive = new DriveInfo(root);
            if (drive.IsReady)
            {
                freeSpace = drive.AvailableFreeSpace;
            }
        }
        catch
        {
            freeSpace = -1;
        }

        var usage = new StorageUsageInfo(
            RootStoragePath,
            wallpaperCount,
            mediaSize,
            thumbnailSize,
            totalSize,
            freeSpace);

        return Task.FromResult(usage);
    }

    public async Task<bool> MigrateStorageRootAsync(string newRootPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newRootPath);
        string targetRoot = Path.GetFullPath(newRootPath);

        string oldRoot;
        lock (_syncLock)
        {
            oldRoot = _rootStoragePath;
            if (string.Equals(oldRoot, targetRoot, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        _logger.LogInformation("Starting storage migration from '{OldRoot}' to '{NewRoot}'.", oldRoot, targetRoot);

        try
        {
            if (!Directory.Exists(targetRoot))
            {
                Directory.CreateDirectory(targetRoot);
            }

            string targetMedia = Path.Combine(targetRoot, "Media");
            string targetThumbs = Path.Combine(targetRoot, "Thumbnails");
            string targetTemp = Path.Combine(targetRoot, "Temp");

            Directory.CreateDirectory(targetMedia);
            Directory.CreateDirectory(targetThumbs);
            Directory.CreateDirectory(targetTemp);

            // Copy Media directory contents
            if (Directory.Exists(MediaDirectory))
            {
                CopyDirectoryRecursive(MediaDirectory, targetMedia, ct);
            }

            // Copy Thumbnails directory contents
            if (Directory.Exists(ThumbnailsDirectory))
            {
                CopyDirectoryRecursive(ThumbnailsDirectory, targetThumbs, ct);
            }

            // Verify file counts in destination match source
            int sourceFiles = Directory.Exists(MediaDirectory) ? Directory.GetFiles(MediaDirectory, "*", SearchOption.AllDirectories).Length : 0;
            int targetFiles = Directory.GetFiles(targetMedia, "*", SearchOption.AllDirectories).Length;

            if (sourceFiles != targetFiles)
            {
                throw new InvalidOperationException($"Storage migration verification failed: source had {sourceFiles} files, target has {targetFiles}.");
            }

            // Atomically switch active root
            lock (_syncLock)
            {
                _rootStoragePath = targetRoot;
            }

            // Safe cleanup of old media and thumbnails
            try
            {
                if (Directory.Exists(Path.Combine(oldRoot, "Media")))
                {
                    Directory.Delete(Path.Combine(oldRoot, "Media"), recursive: true);
                }
                if (Directory.Exists(Path.Combine(oldRoot, "Thumbnails")))
                {
                    Directory.Delete(Path.Combine(oldRoot, "Thumbnails"), recursive: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Migration succeeded, but old directory cleanup had non-critical errors.");
            }

            _logger.LogInformation("Storage root migration completed successfully to '{NewRoot}'.", targetRoot);
            return await Task.FromResult(true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Storage migration failed. Preserving original storage root at '{OldRoot}'.", oldRoot);
            return false;
        }
    }

    public Task CleanTempFilesAsync(TimeSpan? olderThan = null, CancellationToken ct = default)
    {
        if (!Directory.Exists(TempDirectory))
        {
            return Task.CompletedTask;
        }

        var threshold = DateTime.UtcNow - (olderThan ?? TimeSpan.FromHours(1));

        try
        {
            foreach (var file in Directory.GetFiles(TempDirectory))
            {
                try
                {
                    if (File.GetCreationTimeUtc(file) < threshold)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // Ignore transient lock errors on temp cleanup
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exception during temp directory cleanup.");
        }

        return Task.CompletedTask;
    }

    private void EnsureBaseDirectories()
    {
        if (!Directory.Exists(RootStoragePath))
        {
            Directory.CreateDirectory(RootStoragePath);
        }

        if (!Directory.Exists(MediaDirectory))
        {
            Directory.CreateDirectory(MediaDirectory);
        }

        if (!Directory.Exists(ThumbnailsDirectory))
        {
            Directory.CreateDirectory(ThumbnailsDirectory);
        }

        if (!Directory.Exists(TempDirectory))
        {
            Directory.CreateDirectory(TempDirectory);
        }
    }

    private static long CalculateDirectorySize(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return 0;
        }

        try
        {
            var dirInfo = new DirectoryInfo(directoryPath);
            return dirInfo.EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length);
        }
        catch
        {
            return 0;
        }
    }

    private static void CopyDirectoryRecursive(string sourceDir, string targetDir, CancellationToken ct)
    {
        Directory.CreateDirectory(targetDir);

        foreach (string file in Directory.GetFiles(sourceDir))
        {
            ct.ThrowIfCancellationRequested();
            string destFile = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (string subDir in Directory.GetDirectories(sourceDir))
        {
            ct.ThrowIfCancellationRequested();
            string destSubDir = Path.Combine(targetDir, Path.GetFileName(subDir));
            CopyDirectoryRecursive(subDir, destSubDir, ct);
        }
    }
}
