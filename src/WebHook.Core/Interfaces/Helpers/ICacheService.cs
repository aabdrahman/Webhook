namespace WebHook.Core.Interfaces.Helpers;

/// <summary>
/// A two-tier application cache: an in-memory cache in front of a SQLite table that survives restarts.
/// </summary>
/// <remarks>
/// Keys are matched case-insensitively in the persistent tier (they are stored upper-cased) but
/// case-sensitively in the memory tier, so callers should use one spelling per key. Values are
/// serialized to JSON for storage. Methods never throw for cache failures: errors are logged and the
/// operation reports a miss or <see langword="false"/>.
/// </remarks>
public interface ICacheService
{
    /// <summary>
    /// Caches a value under a key.
    /// </summary>
    /// <remarks>
    /// The value is available from the memory tier as soon as the call returns. It is written to the
    /// persistent tier afterwards by a background worker, so a crash in between can lose the persisted copy.
    /// Setting an existing key replaces its value and expiry.
    /// </remarks>
    /// <typeparam name="T">The type of the value. It must be serializable with <c>System.Text.Json</c>.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="value">The value to cache.</param>
    /// <param name="expiresAfter">How long the entry lives, in seconds. Defaults to 300 (5 minutes).</param>
    /// <returns>
    /// <see langword="true"/> if the value was accepted for caching; <see langword="false"/> if it could not be
    /// queued for persistence or an error occurred.
    /// </returns>
    Task<bool> SetCacheItemAsync<T>(string key, T value, long expiresAfter = 300);

    /// <summary>
    /// Gets a cached value, checking memory first and then the persistent tier.
    /// </summary>
    /// <typeparam name="T">The type to return the value as.</typeparam>
    /// <param name="cacheKey">The cache key.</param>
    /// <returns>
    /// The cached value, or <see langword="default"/> if the key is missing, expired, cannot be deserialized
    /// as <typeparamref name="T"/>, or an error occurred.
    /// </returns>
    Task<T?> GetItemsFromCacheAsync<T>(string cacheKey);

    /// <summary>
    /// Removes the given keys from both tiers. Keys that are not cached are ignored.
    /// </summary>
    /// <param name="keys">The cache keys to remove.</param>
    /// <returns>A task that completes when the keys have been removed.</returns>
    Task RemoveItemsFromCacheAsync(params string[] keys);
}
