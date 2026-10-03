using Microsoft.Extensions.Logging;

namespace Lumen.Infrastructure.Persistence.Database;

/// <summary>
/// Executes idempotent schema migrations for the local SQLite metadata database.
/// </summary>
public sealed class DatabaseMigrator
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<DatabaseMigrator> _logger;

    public DatabaseMigrator(IDbConnectionFactory connectionFactory, ILogger<DatabaseMigrator> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task MigrateAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Verifying and executing SQLite database migrations.");

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;

            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS SchemaVersion (
                    Version INTEGER PRIMARY KEY,
                    AppliedAtUtc TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS Wallpapers (
                    Id TEXT PRIMARY KEY,
                    Title TEXT NOT NULL,
                    Description TEXT NOT NULL DEFAULT '',
                    Type INTEGER NOT NULL,
                    FilePath TEXT NOT NULL,
                    ThumbnailPath TEXT NOT NULL DEFAULT '',
                    PreviewPath TEXT NOT NULL DEFAULT '',
                    ContentHash TEXT NOT NULL,
                    Width INTEGER NOT NULL,
                    Height INTEGER NOT NULL,
                    Fps REAL NOT NULL,
                    DurationMs INTEGER NOT NULL,
                    FileSizeBytes INTEGER NOT NULL,
                    Creator TEXT NOT NULL DEFAULT 'Local',
                    Category TEXT NOT NULL DEFAULT 'General',
                    IsFavorite INTEGER NOT NULL DEFAULT 0,
                    IsSubscribed INTEGER NOT NULL DEFAULT 0,
                    CreatedAtUtc TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL
                );

                CREATE UNIQUE INDEX IF NOT EXISTS IX_Wallpapers_ContentHash ON Wallpapers (ContentHash);
                CREATE INDEX IF NOT EXISTS IX_Wallpapers_Category ON Wallpapers (Category);
                CREATE INDEX IF NOT EXISTS IX_Wallpapers_IsFavorite ON Wallpapers (IsFavorite);
                CREATE INDEX IF NOT EXISTS IX_Wallpapers_IsSubscribed ON Wallpapers (IsSubscribed);

                CREATE TABLE IF NOT EXISTS WallpaperTags (
                    WallpaperId TEXT NOT NULL,
                    Tag TEXT NOT NULL,
                    PRIMARY KEY (WallpaperId, Tag),
                    FOREIGN KEY (WallpaperId) REFERENCES Wallpapers(Id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS IX_WallpaperTags_Tag ON WallpaperTags (Tag);

                CREATE TABLE IF NOT EXISTS MonitorAssignments (
                    MonitorDeviceId TEXT PRIMARY KEY,
                    WallpaperId TEXT NOT NULL,
                    AssignedAtUtc TEXT NOT NULL,
                    FOREIGN KEY (WallpaperId) REFERENCES Wallpapers(Id) ON DELETE CASCADE
                );
            ";

            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);

            _logger.LogInformation("Database migration completed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply database migrations. Rolling back.");
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }
}
