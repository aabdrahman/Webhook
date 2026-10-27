namespace WebHook.Core.Entities.ApplicationCache;

public class ApplicationCacheLayerEntity
{
    //public int Id { get; set; }
    public string CacheKey { get; set; }
    public string CacheValue { get; set; }
    public long CreatedAt { get; set; }
    public long ExpiresAt { get; set; }
}
