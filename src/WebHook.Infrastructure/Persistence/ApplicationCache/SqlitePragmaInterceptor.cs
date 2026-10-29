using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace WebHook.Infrastructure.Persistence.ApplicationCache;

/// <summary>
/// Applies SQLite PRAGMA tuning every time a connection is opened.
/// Most PRAGMAs (synchronous, temp_store, cache_size, mmap_size) are per-connection,
/// so they must be re-applied on each open rather than once in a migration.
/// </summary>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    private const string PragmaSql = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA temp_store=MEMORY; PRAGMA cache_size=-20000; PRAGMA mmap_size=268435456;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText = PragmaSql;
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = PragmaSql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
