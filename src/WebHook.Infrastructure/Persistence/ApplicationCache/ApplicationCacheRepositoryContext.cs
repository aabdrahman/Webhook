using Microsoft.EntityFrameworkCore;
using WebHook.Core.Entities.ApplicationCache;

namespace WebHook.Infrastructure.Persistence.ApplicationCache;

public class ApplicationCacheRepositoryContext : DbContext
{
    public ApplicationCacheRepositoryContext(DbContextOptions options) : base(options)
    {
    }

    public DbSet<ApplicationCacheLayerEntity> ApplicationCacheLayerEntities { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicationCacheLayerEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(x => x.CacheKey).IsUnique();
            entity.HasIndex(x => x.ExpiresAt);
            entity.Property(e => e.CacheKey).HasMaxLength(100).IsRequired();
            entity.Property(e => e.CacheValue).HasColumnType("blob").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("integer").IsRequired();
            entity.Property(e => e.ExpiresAt).HasColumnType("integer").IsRequired();
        });
    }
}
