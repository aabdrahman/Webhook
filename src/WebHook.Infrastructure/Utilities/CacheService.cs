using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Serilog;
using System.Text.Json;
using WebHook.Core.DataTransferObjects.ApplicationCache;
using WebHook.Core.EventContracts.Publishers;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Infrastructure.Persistence.ApplicationCache;

namespace WebHook.Infrastructure.Utilities;

public sealed class CacheService : ICacheService
{
    private readonly IMemoryCache _memoryCache;
    private readonly ApplicationCacheRepositoryContext _applicationCacheRepositoryContext;
    private readonly IApplicationPublisher _applicationPublisher;

    public CacheService(IMemoryCache memoryCache, ApplicationCacheRepositoryContext applicationCacheRepositoryContext, IApplicationPublisher applicationPublisher)
    {
        _memoryCache = memoryCache;
        _applicationCacheRepositoryContext = applicationCacheRepositoryContext;
        _applicationPublisher = applicationPublisher;

        _logger = Log.ForContext("ClassName", nameof(CacheService));
    }

    private static SemaphoreSlim _readCacheLock = new SemaphoreSlim(1, 1);

    private ILogger _logger;

    public async Task<T?> GetItemsFromCacheAsync<T>(string cacheKey)
    {
        _logger = _logger.ForContext("MethodName", nameof(GetItemsFromCacheAsync));
        try
        {
            _logger.Information("Getting cached item for key - {0}", cacheKey);

            var isCacheExist = _memoryCache.TryGetValue<T>(cacheKey, out var cachedItem);

            if(isCacheExist)
            {
                _logger.Information("Item found in cache for key - {0}, value - {1}", cacheKey, cachedItem);
                return cachedItem;
            }

            var result = await _applicationCacheRepositoryContext.ApplicationCacheLayerEntities
                .Where(x => x.CacheKey == cacheKey && x.ExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                .Select(x => x.CacheValue).FirstOrDefaultAsync();

            return result != null ? JsonSerializer.Deserialize<T>(result) : default(T?);

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while getting item fromm cache.");
            return default(T?);
        }
    }

    public async Task RemoveItemsFromCacheAsync(params string[] keys)
    {
        _logger.ForContext("MethodName", nameof(RemoveItemsFromCacheAsync)).Information("Removing cached items from cache - {0}", keys);

        foreach (var key in keys)
        {
            _memoryCache.Remove(key);
        }

        var removedItems = await _applicationCacheRepositoryContext.ApplicationCacheLayerEntities
                                        .Where(x => keys.Contains(x.CacheKey))
                                        .ExecuteDeleteAsync();

        _logger.Information("Removed {0} items from cache for keys - {1}", removedItems, keys);


    }

    public async Task<bool> SetCacheItemAsync<T>(string key, T value, long expiresAfter = 300)
    {
        _logger = _logger.ForContext("MethodName", nameof(SetCacheItemAsync));
        try
        {
            _logger.Information("Setting cached item for key - {0}, value - {1}", key, value);
            await Task.Delay(1);

            var isQueued = await _applicationPublisher.QueueItemToCache(new ApplicationCacheEvent(cacheKey: key, cacheValue: JsonSerializer.Serialize(value), cacheExpiration: expiresAfter));

            if (isQueued)
            {
                return isQueued;
            }

            //var cacheResult = _memoryCache.Set<T>(key, value);
            var cacheResult = _memoryCache.Set<T>(key, value, new MemoryCacheEntryOptions
            {
                Size = 1, //This memory cache option is necessary as the caching fails otherwise, also the 1unit ensures that the cache uses the defined value in the DI.
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(int.MaxValue),
                SlidingExpiration = TimeSpan.FromMinutes(int.MaxValue)
            });

            _logger.Information("Item set in cache successfully. Key: {0}, Value: {1}, Result: {2}", key, value, cacheResult);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while setting item to cache. Key: {0}, value: {1}", key, value);
            return false;
        }

    }
}
