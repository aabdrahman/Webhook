namespace WebHook.Core.DataTransferObjects.ApplicationCache;

/// <summary>
/// A request to persist a cache entry, queued by the cache service and processed by the cache persistence worker.
/// </summary>
/// <param name="cacheKey">The cache key as supplied by the caller. The worker upper-cases it for storage.</param>
/// <param name="cacheValue">The value, already serialized as JSON.</param>
/// <param name="cacheExpiration">How long the entry lives, in seconds, counted from when the worker persists it. Defaults to 300.</param>
public record class ApplicationCacheEvent
(string cacheKey, string cacheValue, long cacheExpiration = 300);
