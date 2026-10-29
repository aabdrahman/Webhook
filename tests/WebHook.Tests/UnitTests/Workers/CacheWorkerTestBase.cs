using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebHook.Infrastructure.Persistence.ApplicationCache;

namespace WebHook.Tests.UnitTests.Workers;

/// <summary>
/// Shared setup for the cache worker tests: an open in-memory SQLite connection holding the cache table,
/// and a service provider whose scopes hand out <see cref="ApplicationCacheRepositoryContext"/> instances on it.
/// </summary>
public abstract class CacheWorkerTestBase : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"cache-worker-tests-{Guid.NewGuid():N}.db");
    private string ConnectionString => $"Data Source={_dbPath};Pooling=False";
    protected readonly ServiceProvider Services;

    protected CacheWorkerTestBase()
    {
        CreateTable();

        Services = new ServiceCollection()
            .AddDbContext<ApplicationCacheRepositoryContext>(opts => opts.UseSqlite(ConnectionString))
            .BuildServiceProvider();
    }

    protected IServiceScopeFactory ScopeFactory => Services.GetRequiredService<IServiceScopeFactory>();

    protected void CreateTable()
    {
        Execute(@"CREATE TABLE IF NOT EXISTS ApplicationCacheLayerEntities (
                      CacheKey TEXT NOT NULL,
                      CacheValue BLOB NOT NULL,
                      CreatedAt INTEGER NOT NULL,
                      ExpiresAt INTEGER NOT NULL);
                  CREATE UNIQUE INDEX IF NOT EXISTS IX_ApplicationCacheLayerEntities_CacheKey ON ApplicationCacheLayerEntities (CacheKey);");
    }

    protected void DropTable() => Execute("DROP TABLE ApplicationCacheLayerEntities");

    protected void InsertRow(string key, string value, long expiresAt)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO ApplicationCacheLayerEntities (CacheKey, CacheValue, CreatedAt, ExpiresAt) VALUES ($key, $value, $createdAt, $expiresAt)";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        command.Parameters.AddWithValue("$expiresAt", expiresAt);
        command.ExecuteNonQuery();
    }

    protected List<string> Keys()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT CacheKey FROM ApplicationCacheLayerEntities ORDER BY CacheKey";
        using var reader = command.ExecuteReader();
        var keys = new List<string>();
        while (reader.Read()) keys.Add(reader.GetString(0));
        return keys;
    }

    protected (string Value, long CreatedAt, long ExpiresAt)? GetRow(string key)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT CacheValue, CreatedAt, ExpiresAt FROM ApplicationCacheLayerEntities WHERE CacheKey = $key";
        command.Parameters.AddWithValue("$key", key);
        using var reader = command.ExecuteReader();
        return reader.Read() ? (reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2)) : null;
    }

    protected static long InFuture(int seconds = 300) => DateTimeOffset.UtcNow.AddSeconds(seconds).ToUnixTimeSeconds();
    protected static long InPast(int seconds = 300) => DateTimeOffset.UtcNow.AddSeconds(-seconds).ToUnixTimeSeconds();

    /// <summary>Polls until <paramref name="condition"/> holds or the timeout elapses, then returns its final value.</summary>
    protected static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(25);
        }
        return condition();
    }

    // Each helper call uses its own connection: the worker runs on another thread and a SqliteConnection is not thread-safe.
    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    private void Execute(string sql)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public virtual void Dispose()
    {
        Services.Dispose();
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm", _dbPath + "-journal" })
            if (File.Exists(file)) File.Delete(file);
    }
}
