using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Threading.Channels;
using WebHook.Core.DataTransferObjects.ApplicationCache;
using WebHook.Infrastructure.Persistence.ApplicationCache;

namespace WebHook.Infrastructure.BackgroundWorkers;

public class CachePersistenceWorker : BackgroundService
{
    private readonly Channel<ApplicationCacheEvent> _cacheEventChannel;
    private readonly IMemoryCache _memeoryCache;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public CachePersistenceWorker(Channel<ApplicationCacheEvent> cacheEventChannel, IServiceScopeFactory serviceScopeFactory, IMemoryCache memeoryCache)
    {
        _cacheEventChannel = cacheEventChannel;
        _serviceScopeFactory = serviceScopeFactory;
        _memeoryCache = memeoryCache;
    }

    private Serilog.ILogger _logger = Serilog.Log.ForContext("ClassName", nameof(CachePersistenceWorker));
    private const string _methodName = "MethodName";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger = _logger.ForContext(_methodName, nameof(ExecuteAsync));

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            _logger.Information("Begin processing cache event channel........");

            await foreach (var cacheItem in _cacheEventChannel.Reader.ReadAllAsync(stoppingToken))
            {
                _logger.Information("Begin processing queued cache event from channel - {0}", cacheItem);

                using var scope = _serviceScopeFactory.CreateScope();

                var ctx = scope.ServiceProvider.GetRequiredService<ApplicationCacheRepositoryContext>();

                var operationTimestamp = DateTimeOffset.UtcNow;
                var cacheKeyParameter = new SqliteParameter("@Key", cacheItem.cacheKey.ToUpper());
                var cacheValueParameter = new SqliteParameter("@Value", cacheItem.cacheValue);
                var cacheExpirationParameter = new SqliteParameter("@ExpiresAt", operationTimestamp.AddSeconds(cacheItem.cacheExpiration).ToUnixTimeSeconds());
                var cacheCreatedAtParameter = new SqliteParameter("@CreatedAt", operationTimestamp.ToUnixTimeSeconds());

                var persistence = await ctx.Database.ExecuteSqlRawAsync(sql: "INSERT OR REPLACE INTO ApplicationCacheLayerEntities (CacheKey, CacheValue, ExpiresAt, CreatedAt) VALUES (@Key,@Value,@ExpiresAt,@CreatedAt)", 
                    [cacheKeyParameter, cacheValueParameter, cacheExpirationParameter, cacheCreatedAtParameter], cancellationToken: stoppingToken);
                var cacheResult = _memeoryCache.Set<string>(cacheItem.cacheKey, cacheItem.cacheValue, new MemoryCacheEntryOptions
                {
                    Size = 1, //This memory cache option is necessary as the caching fails otherwise, also the 1unit ensures that the cache uses the defined value in the DI.
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(int.MaxValue),
                    SlidingExpiration = TimeSpan.FromMinutes(cacheItem.cacheExpiration)
                });
                _logger.Information("Cache Item persisted affected {0} row(s) in db. Returns: {1} from memeory cache", persistence, cacheResult);

                await Task.Delay(10);
            }
        }
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger = _logger.ForContext(_methodName, nameof(StartAsync));

        _logger.Information("Cache Event Worker Processor is starting.......");

        using var scope = _serviceScopeFactory.CreateScope();
        var cacheRepoCtx = scope.ServiceProvider.GetRequiredService<ApplicationCacheRepositoryContext>();
        //var staleItemsDeleted = await cacheRepoCtx.ApplicationCacheLayerEntities.Where(x => x.ExpiresAt < DateTimeOffset.UtcNow.ToUnixTimeSeconds()).ExecuteDeleteAsync(cancellationToken);
        var staleItemsDeleted = await cacheRepoCtx.Database.ExecuteSqlRawAsync(@"DELETE FROM ApplicationCacheLayerEntities WHERE ExpiresAt < {0}", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        _logger.Information("Total Stale Items deleted - {0}", staleItemsDeleted);

        await base.StartAsync(cancellationToken);
    }
}
