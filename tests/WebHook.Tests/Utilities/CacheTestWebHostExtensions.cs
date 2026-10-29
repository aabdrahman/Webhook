using Microsoft.AspNetCore.Hosting;

namespace WebHook.Tests.Utilities;

/// <summary>
/// Points a <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/> host at a throwaway
/// SQLite cache database. The real app starts <c>CachePersistenceWorker</c>, which deletes stale rows on startup and
/// fails if the cache table does not exist, so the default <c>applicationcache.db</c> (which has no migrations
/// applied in a test run) would stop every host from starting.
/// </summary>
public static class CacheTestWebHostExtensions
{
    private const string ConnectionStringKey = "ConnectionStrings:ApplicationCacheDbConnection";

    // One database per test process is enough: factories only need the table to exist.
    private static readonly Lazy<string> SharedConnectionString = new(CreateSharedDatabase);

    public static IWebHostBuilder UseTemporaryCacheDatabase(this IWebHostBuilder builder)
        => builder.UseSetting(ConnectionStringKey, SharedConnectionString.Value);

    private static string CreateSharedDatabase()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"app-cache-webhost-tests-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath};Pooling=False";

        CacheTestServiceExtensions.CreateCacheTable(connectionString);

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            foreach (var file in new[] { dbPath, dbPath + "-wal", dbPath + "-shm", dbPath + "-journal" })
            {
                try { if (File.Exists(file)) File.Delete(file); } catch (IOException) { /* best effort */ }
            }
        };

        return connectionString;
    }
}
