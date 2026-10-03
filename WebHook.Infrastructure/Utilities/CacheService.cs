using Microsoft.Extensions.Caching.Memory;
using Serilog;
using WebHook.Core.Interfaces.Helpers;

namespace WebHook.Infrastructure.Utilities;

public sealed class CacheService : ICacheService
{
    private readonly IMemoryCache _memoryCache;

    public CacheService(IMemoryCache memoryCache)
    {
        _memoryCache = memoryCache;

        _logger = Log.ForContext("ClassName", nameof(CacheService));
    }

    private ILogger _logger;

    public async Task<T?> GetItemsFromCacheAsync<T>(string cacheKey)
    {
        _logger = _logger.ForContext("MethodName", nameof(GetItemsFromCacheAsync));
        try
        {
            _logger.Information("Getting cached item for key - {0}", cacheKey);
            await Task.Delay(1);
            var isCacheExist = _memoryCache.TryGetValue<T>(cacheKey, out var cachedItem);

            return isCacheExist ? cachedItem : default(T?);
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
        await Task.Delay(1);
        foreach (var key in keys)
        {
            _memoryCache.Remove(key);
        }
        
    }

    public async Task<bool> SetCacheItemAsync<T>(string key, T value)
    {
        _logger = _logger.ForContext("MethodName", nameof(SetCacheItemAsync));
        try
        {
            _logger.Information("Setting cached item for key - {0}, value - {1}", key, value);
            await Task.Delay(1);

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
