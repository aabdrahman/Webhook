namespace WebHook.Core.Entities.ApplicationCache;

/// <summary>
/// A row in the persistent tier of the application cache (the <c>ApplicationCacheLayerEntities</c> table
/// in the separate SQLite cache database).
/// </summary>
/// <remarks>
/// The entity has no primary key. Rows are written and deleted with raw SQL, and
/// <see cref="CacheKey"/> is protected by a unique index so <c>INSERT OR REPLACE</c> overwrites a key.
/// </remarks>
public class ApplicationCacheLayerEntity
{
    //public int Id { get; set; }

    /// <summary>The upper-cased cache key. Unique, at most 100 characters.</summary>
    public string CacheKey { get; set; }

    /// <summary>The cached value serialized as JSON.</summary>
    public string CacheValue { get; set; }

    /// <summary>When the row was written, as Unix time in seconds (UTC).</summary>
    public long CreatedAt { get; set; }

    /// <summary>When the row stops being valid, as Unix time in seconds (UTC). Expired rows are ignored by reads and deleted by the eviction worker.</summary>
    public long ExpiresAt { get; set; }
}
