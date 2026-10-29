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

    /// <summary>Applies the PRAGMAs to a synchronously opened connection.</summary>
    /// <param name="connection">The connection that was just opened.</param>
    /// <param name="eventData">Details of the open event.</param>
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText = PragmaSql;
        command.ExecuteNonQuery();
    }

    /// <summary>Applies the PRAGMAs to an asynchronously opened connection.</summary>
    /// <param name="connection">The connection that was just opened.</param>
    /// <param name="eventData">Details of the open event.</param>
    /// <param name="cancellationToken">A token to cancel the command.</param>
    /// <returns>A task that completes when the PRAGMAs have run.</returns>
    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = PragmaSql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
