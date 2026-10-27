namespace WebHook.Core.Interfaces.Helpers;

public interface ICacheService
{
    Task<bool> SetCacheItemAsync<T>(string key, T value, long expiresAfter = 300);
    Task<T?> GetItemsFromCacheAsync<T>(string cacheKey);
    Task RemoveItemsFromCacheAsync(params string[] keys);
}

