using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Threading.Channels;
using WebHook.Core.DataTransferObjects.ApplicationCache;
using WebHook.Core.DataTransferObjects.EmailSender;
using WebHook.Core.EventContracts.Events;
using WebHook.Core.EventContracts.Publishers;
using WebHook.Infrastructure.EventPublishers;
using WebHook.Infrastructure.Persistence.ApplicationCache;

namespace WebHook.Tests.Utilities;

/// <summary>
/// Registers everything the real <c>CacheService</c> needs to be resolved from a test service provider:
/// the SQLite-backed <see cref="ApplicationCacheRepositoryContext"/> (with its table created) and the
/// channels required by <c>ApplicationPublisher</c>. Existing registrations are left alone.
/// </summary>
public static class CacheTestServiceExtensions
{
    /// <summary>
    /// Adds a temporary SQLite cache database and the publisher channels. The database file is deleted
    /// when the service provider is disposed. No cache workers run, so writes stay in the memory cache
    /// and the channel; reads are served from memory.
    /// </summary>
    public static IServiceCollection AddApplicationCacheForTests(this IServiceCollection services)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"app-cache-tests-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath};Pooling=False";

        CreateCacheTable(connectionString);

        services.AddDbContext<ApplicationCacheRepositoryContext>(opts => opts.UseSqlite(connectionString));
        services.AddSingleton(new CacheDatabaseCleanup(dbPath));

        services.TryAddSingleton(_ => Channel.CreateUnbounded<EventRaised>());
        services.TryAddSingleton(_ => Channel.CreateBounded<EmailSenderDto>(new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.Wait }));
        services.TryAddSingleton(_ => Channel.CreateBounded<ApplicationCacheEvent>(new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.Wait }));
        services.TryAddSingleton<IApplicationPublisher, ApplicationPublisher>();

        return services;
    }

    // Mirrors the schema produced by the ApplicationCache migrations.
    internal static void CreateCacheTable(string connectionString)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"CREATE TABLE ApplicationCacheLayerEntities (
                                    CacheKey TEXT NOT NULL,
                                    CacheValue BLOB NOT NULL,
                                    CreatedAt INTEGER NOT NULL,
                                    ExpiresAt INTEGER NOT NULL);
                                CREATE UNIQUE INDEX IX_ApplicationCacheLayerEntities_CacheKey ON ApplicationCacheLayerEntities (CacheKey);";
        command.ExecuteNonQuery();
    }

    private sealed class CacheDatabaseCleanup(string dbPath) : IDisposable
    {
        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            foreach (var file in new[] { dbPath, dbPath + "-wal", dbPath + "-shm", dbPath + "-journal" })
            {
                try { if (File.Exists(file)) File.Delete(file); } catch (IOException) { /* best effort */ }
            }
        }
    }
}
