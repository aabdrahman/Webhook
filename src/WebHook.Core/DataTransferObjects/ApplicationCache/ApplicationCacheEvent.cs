namespace WebHook.Core.DataTransferObjects.ApplicationCache;

public record class ApplicationCacheEvent
(string cacheKey, string cacheValue, long cacheExpiration = 300);
