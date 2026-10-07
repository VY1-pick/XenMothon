using XenMotion.Core.Interfaces;
using System.Data.Common;
using System.Globalization;
using XenMotion.Core.Enums;
using XenMotion.Core.Models;
using Microsoft.Extensions.Logging;

namespace XenMotion.Infrastructure.Persistence.Database;

/// <summary>
/// Parameterized SQLite repository implementation for querying and mutating wallpaper metadata.
/// </summary>
public sealed class WallpaperRepository : IWallpaperRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<WallpaperRepository> _logger;

    public WallpaperRepository(IDbConnectionFactory connectionFactory, ILogger<WallpaperRepository> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<Wallpaper>> GetAllAsync(CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Wallpapers ORDER BY CreatedAtUtc DESC;";

        return await ReadWallpapersAsync(cmd, connection, ct).ConfigureAwait(false);
    }

    public async Task<Wallpaper?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Wallpapers WHERE Id = @Id LIMIT 1;";
        AddParameter(cmd, "@Id", id.ToString());

        var list = await ReadWallpapersAsync(cmd, connection, ct).ConfigureAwait(false);
        return list.Count > 0 ? list[0] : null;
    }

    public async Task<Wallpaper?> GetByContentHashAsync(string contentHash, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Wallpapers WHERE ContentHash = @ContentHash LIMIT 1;";
        AddParameter(cmd, "@ContentHash", contentHash);

        var list = await ReadWallpapersAsync(cmd, connection, ct).ConfigureAwait(false);
        return list.Count > 0 ? list[0] : null;
    }

    public async Task<IReadOnlyList<Wallpaper>> SearchAsync(
        string query,
        string? category = null,
        IEnumerable<string>? tags = null,
        CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();

        var sql = "SELECT DISTINCT w.* FROM Wallpapers w LEFT JOIN WallpaperTags t ON w.Id = t.WallpaperId WHERE 1=1";

        if (!string.IsNullOrWhiteSpace(query))
        {
            sql += " AND (w.Title LIKE @Query OR w.Description LIKE @Query OR t.Tag LIKE @Query)";
            AddParameter(cmd, "@Query", $"%{query.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            sql += " AND w.Category = @Category";
            AddParameter(cmd, "@Category", category);
        }

        sql += " ORDER BY w.CreatedAtUtc DESC;";
        cmd.CommandText = sql;

        return await ReadWallpapersAsync(cmd, connection, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Wallpaper>> GetSubscribedAsync(CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Wallpapers WHERE IsSubscribed = 1 ORDER BY CreatedAtUtc DESC;";

        return await ReadWallpapersAsync(cmd, connection, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Wallpaper>> GetFavoritesAsync(CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Wallpapers WHERE IsFavorite = 1 ORDER BY CreatedAtUtc DESC;";

        return await ReadWallpapersAsync(cmd, connection, ct).ConfigureAwait(false);
    }

    public async Task InsertAsync(Wallpaper wallpaper, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(wallpaper);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                INSERT INTO Wallpapers (
                    Id, Title, Description, Type, FilePath, ThumbnailPath, PreviewPath,
                    ContentHash, Width, Height, Fps, DurationMs, FileSizeBytes,
                    Creator, Category, IsFavorite, IsSubscribed, CreatedAtUtc, UpdatedAtUtc
                ) VALUES (
                    @Id, @Title, @Description, @Type, @FilePath, @ThumbnailPath, @PreviewPath,
                    @ContentHash, @Width, @Height, @Fps, @DurationMs, @FileSizeBytes,
                    @Creator, @Category, @IsFavorite, @IsSubscribed, @CreatedAtUtc, @UpdatedAtUtc
                );
            ";

            AddParameter(cmd, "@Id", wallpaper.Id.ToString());
            AddParameter(cmd, "@Title", wallpaper.Title);
            AddParameter(cmd, "@Description", wallpaper.Description);
            AddParameter(cmd, "@Type", (int)wallpaper.Type);
            AddParameter(cmd, "@FilePath", wallpaper.FilePath);
            AddParameter(cmd, "@ThumbnailPath", wallpaper.ThumbnailPath);
            AddParameter(cmd, "@PreviewPath", wallpaper.PreviewPath);
            AddParameter(cmd, "@ContentHash", wallpaper.ContentHash);
            AddParameter(cmd, "@Width", wallpaper.Width);
            AddParameter(cmd, "@Height", wallpaper.Height);
            AddParameter(cmd, "@Fps", wallpaper.Fps);
            AddParameter(cmd, "@DurationMs", wallpaper.DurationMs);
            AddParameter(cmd, "@FileSizeBytes", wallpaper.FileSizeBytes);
            AddParameter(cmd, "@Creator", wallpaper.Creator);
            AddParameter(cmd, "@Category", wallpaper.Category);
            AddParameter(cmd, "@IsFavorite", wallpaper.IsFavorite ? 1 : 0);
            AddParameter(cmd, "@IsSubscribed", wallpaper.IsSubscribed ? 1 : 0);
            AddParameter(cmd, "@CreatedAtUtc", wallpaper.CreatedAtUtc.ToString("o", CultureInfo.InvariantCulture));
            AddParameter(cmd, "@UpdatedAtUtc", wallpaper.UpdatedAtUtc.ToString("o", CultureInfo.InvariantCulture));

            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            // Insert tags
            if (wallpaper.Tags.Count > 0)
            {
                foreach (var tag in wallpaper.Tags)
                {
                    await using var tagCmd = connection.CreateCommand();
                    tagCmd.Transaction = transaction;
                    tagCmd.CommandText = "INSERT OR IGNORE INTO WallpaperTags (WallpaperId, Tag) VALUES (@WallpaperId, @Tag);";
                    AddParameter(tagCmd, "@WallpaperId", wallpaper.Id.ToString());
                    AddParameter(tagCmd, "@Tag", tag.Trim().ToLowerInvariant());
                    await tagCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
            _logger.LogInformation("Inserted wallpaper metadata for '{Title}' ({Id}).", wallpaper.Title, wallpaper.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to insert wallpaper '{Title}'. Rolling back transaction.", wallpaper.Title);
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    public async Task UpdateAsync(Wallpaper wallpaper, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(wallpaper);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE Wallpapers SET
                Title = @Title,
                Description = @Description,
                ThumbnailPath = @ThumbnailPath,
                PreviewPath = @PreviewPath,
                Category = @Category,
                IsFavorite = @IsFavorite,
                IsSubscribed = @IsSubscribed,
                UpdatedAtUtc = @UpdatedAtUtc
            WHERE Id = @Id;
        ";

        AddParameter(cmd, "@Id", wallpaper.Id.ToString());
        AddParameter(cmd, "@Title", wallpaper.Title);
        AddParameter(cmd, "@Description", wallpaper.Description);
        AddParameter(cmd, "@ThumbnailPath", wallpaper.ThumbnailPath);
        AddParameter(cmd, "@PreviewPath", wallpaper.PreviewPath);
        AddParameter(cmd, "@Category", wallpaper.Category);
        AddParameter(cmd, "@IsFavorite", wallpaper.IsFavorite ? 1 : 0);
        AddParameter(cmd, "@IsSubscribed", wallpaper.IsSubscribed ? 1 : 0);
        AddParameter(cmd, "@UpdatedAtUtc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Wallpapers WHERE Id = @Id;";
        AddParameter(cmd, "@Id", id.ToString());

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        _logger.LogInformation("Deleted wallpaper record ({Id}) from database.", id);
    }

    private static async Task<IReadOnlyList<Wallpaper>> ReadWallpapersAsync(
        DbCommand cmd,
        DbConnection connection,
        CancellationToken ct)
    {
        var list = new List<Wallpaper>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var wallpaper = new Wallpaper
            {
                Id = Guid.Parse(reader.GetString(reader.GetOrdinal("Id"))),
                Title = reader.GetString(reader.GetOrdinal("Title")),
                Description = reader.GetString(reader.GetOrdinal("Description")),
                Type = (WallpaperType)reader.GetInt32(reader.GetOrdinal("Type")),
                FilePath = reader.GetString(reader.GetOrdinal("FilePath")),
                ThumbnailPath = reader.GetString(reader.GetOrdinal("ThumbnailPath")),
                PreviewPath = reader.GetString(reader.GetOrdinal("PreviewPath")),
                ContentHash = reader.GetString(reader.GetOrdinal("ContentHash")),
                Width = reader.GetInt32(reader.GetOrdinal("Width")),
                Height = reader.GetInt32(reader.GetOrdinal("Height")),
                Fps = reader.GetDouble(reader.GetOrdinal("Fps")),
                DurationMs = reader.GetInt64(reader.GetOrdinal("DurationMs")),
                FileSizeBytes = reader.GetInt64(reader.GetOrdinal("FileSizeBytes")),
                Creator = reader.GetString(reader.GetOrdinal("Creator")),
                Category = reader.GetString(reader.GetOrdinal("Category")),
                IsFavorite = reader.GetInt32(reader.GetOrdinal("IsFavorite")) == 1,
                IsSubscribed = reader.GetInt32(reader.GetOrdinal("IsSubscribed")) == 1,
                CreatedAtUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAtUtc")), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                UpdatedAtUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("UpdatedAtUtc")), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            };

            list.Add(wallpaper);
        }

        return list;
    }

    private static void AddParameter(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
