using System.Data.Common;

namespace XenMotion.Infrastructure.Persistence.Database;

/// <summary>
/// Factory for creating configured, high-throughput SQLite database connections.
/// </summary>
public interface IDbConnectionFactory
{
    DbConnection CreateConnection();
    Task<DbConnection> CreateOpenConnectionAsync(CancellationToken ct = default);
}
