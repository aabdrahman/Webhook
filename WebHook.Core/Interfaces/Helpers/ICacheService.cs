namespace WebHook.Core.Interfaces.Helpers;

public interface ICacheService
{
    Task<bool> SetCacheItemAsync<T>(string key, T value);
    Task<T?> GetItemsFromCacheAsync<T>(string cacheKey);
    Task RemoveItemsFromCacheAsync(params string[] keys);
}

