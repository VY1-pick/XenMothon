using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Lumen.Infrastructure.Persistence.Database;

/// <summary>
/// Creates and initializes SQLite connections configured for concurrent WAL-mode operation.
/// </summary>
public sealed class SqliteConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;
    private readonly ILogger<SqliteConnectionFactory> _logger;

    public SqliteConnectionFactory(string databasePath, ILogger<SqliteConnectionFactory> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };

        _connectionString = builder.ToString();
    }

    public DbConnection CreateConnection()
    {
        return new SqliteConnection(_connectionString);
    }

    public async Task<DbConnection> CreateOpenConnectionAsync(CancellationToken ct = default)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        // Apply performance and safety pragmas
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 5000;
        ";
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        return connection;
    }
}
