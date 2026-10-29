using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Serilog;
using System.Text.Json;
using WebHook.Core.DataTransferObjects.ApplicationCache;
using WebHook.Core.EventContracts.Publishers;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Infrastructure.Persistence.ApplicationCache;

namespace WebHook.Infrastructure.Utilities;

/// <summary>
/// <see cref="ICacheService"/> implementation backed by <see cref="IMemoryCache"/> and a SQLite table.
/// </summary>
/// <remarks>
/// <para>
/// <b>Set:</b> the entry is queued to the cache persistence worker (write-behind) and placed in memory
/// straight away, so reads see it immediately. <b>Get:</b> memory is checked first; on a miss the SQLite table
/// is queried for an unexpired row (the result is not copied back into memory). <b>Remove:</b> keys are deleted
/// from memory and from SQLite.
/// </para>
/// <para>
/// All SQL is raw and parameterised because <see cref="WebHook.Core.Entities.ApplicationCache.ApplicationCacheLayerEntity"/>
/// has no key. Keys are upper-cased for SQLite, so lookups there are case-insensitive. The service is scoped
/// because it uses the scoped cache DbContext.
/// </para>
/// </remarks>
public sealed class CacheService : ICacheService
{
    private readonly IMemoryCache _memoryCache;
    private readonly ApplicationCacheRepositoryContext _applicationCacheRepositoryContext;
    private readonly IApplicationPublisher _applicationPublisher;

    /// <summary>
    /// Initialises a new instance of the <see cref="CacheService"/> class.
    /// </summary>
    /// <param name="memoryCache">The in-memory tier.</param>
    /// <param name="applicationCacheRepositoryContext">The context for the persistent SQLite tier.</param>
    /// <param name="applicationPublisher">Queues entries for the cache persistence worker.</param>
    public CacheService(IMemoryCache memoryCache, ApplicationCacheRepositoryContext applicationCacheRepositoryContext, IApplicationPublisher applicationPublisher)
    {
        _memoryCache = memoryCache;
        _applicationCacheRepositoryContext = applicationCacheRepositoryContext;
        _applicationPublisher = applicationPublisher;

        _logger = Log.ForContext("ClassName", nameof(CacheService));
    }

    private static SemaphoreSlim _readCacheLock = new SemaphoreSlim(1, 1);

    private ILogger _logger;

    /// <inheritdoc />
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

            _logger.Information("Cache Value for: {0} could not be fetched - {1}", cacheKey, cachedItem);

            var result = await _applicationCacheRepositoryContext.Database.SqlQueryRaw<string>(@"SELECT CacheValue AS [Value] FROM ApplicationCacheLayerEntities WHERE CacheKey = {0} AND ExpiresAt > {1}", cacheKey.ToUpper(), DateTimeOffset.UtcNow.ToUnixTimeSeconds()).FirstOrDefaultAsync();

            return result != null ? JsonSerializer.Deserialize<T>(result) : default(T?);

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while getting item fromm cache.");
            return default(T?);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Unlike the other methods this does not catch exceptions: a SQLite failure propagates to the caller.
    /// A set that is still waiting in the persistence channel is not cancelled, so it can write the key again
    /// after this call.
    /// </remarks>
    public async Task RemoveItemsFromCacheAsync(params string[] keys)
    {
        _logger.ForContext("MethodName", nameof(RemoveItemsFromCacheAsync)).Information("Removing cached items from cache - {0}", keys);
        List<string> keysToRemove = [];
        foreach (var key in keys)
        {
            _memoryCache.Remove(key);
            keysToRemove.Add(key.ToUpper());
        }

        var isDeleted = await _applicationCacheRepositoryContext.Database.ExecuteSqlRawAsync(@"DELETE FROM ApplicationCacheLayerEntities WHERE CacheKey IN (SELECT [value] FROM json_each({0}))", JsonSerializer.Serialize(keysToRemove));

        _logger.Information("Removed items from cache for keys - {0}. Total Deleted: {1}", keys, isDeleted);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The memory entry expires <paramref name="expiresAfter"/> seconds after this call. The persisted row
    /// expires the same number of seconds after the worker writes it, which can be slightly later.
    /// </remarks>
    public async Task<bool> SetCacheItemAsync<T>(string key, T value, long expiresAfter = 300)
    {
        _logger = _logger.ForContext("MethodName", nameof(SetCacheItemAsync));
        try
        {
            _logger.Information("Setting cached item for key - {0}, value - {1}", key, value);

            var isQueued = await _applicationPublisher.QueueItemToCache(new ApplicationCacheEvent(cacheKey: key, cacheValue: JsonSerializer.Serialize(value), cacheExpiration: expiresAfter));

            if (isQueued)
            {
                //var cacheResult = _memoryCache.Set<T>(key, value);
                var cacheResult = _memoryCache.Set<T>(key, value, new MemoryCacheEntryOptions
                {
                    Size = 1, //This memory cache option is necessary as the caching fails otherwise, also the 1unit ensures that the cache uses the defined value in the DI.
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(expiresAfter),
                    SlidingExpiration = TimeSpan.FromMinutes(int.MaxValue)
                });

                _logger.Information("Item set in cache successfully. Key: {0}, Value: {1}, Result: {2}", key, value, cacheResult);
                return true;
            }

            _logger.Information("Item could not be set to cache successfully - {0}", isQueued);
            return isQueued;

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while setting item to cache. Key: {0}, value: {1}", key, value);
            return false;
        }

    }
}
