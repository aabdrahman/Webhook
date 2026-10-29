using Microsoft.EntityFrameworkCore;
using WebHook.Core.Entities.ApplicationCache;

namespace WebHook.Infrastructure.Persistence.ApplicationCache;

/// <summary>
/// EF Core context for the persistent tier of the application cache, a SQLite database separate from the
/// main PostgreSQL database (connection string <c>ApplicationCacheDbConnection</c>).
/// </summary>
/// <remarks>
/// The cache code reads and writes with raw SQL, so the entity set is used mainly to define the schema for
/// migrations. Connection tuning (WAL and related PRAGMAs) is applied by <see cref="SqlitePragmaInterceptor"/>,
/// which is registered where the context is added to DI.
/// </remarks>
public class ApplicationCacheRepositoryContext : DbContext
{
    /// <summary>
    /// Initialises a new instance of the <see cref="ApplicationCacheRepositoryContext"/> class.
    /// </summary>
    /// <param name="options">The context options, including the SQLite connection.</param>
    public ApplicationCacheRepositoryContext(DbContextOptions options) : base(options)
    {
    }

    /// <summary>The cache rows. The entity is keyless; see <see cref="ApplicationCacheLayerEntity"/>.</summary>
    public DbSet<ApplicationCacheLayerEntity> ApplicationCacheLayerEntities { get; set; }

    /// <summary>
    /// Configures the cache table: a unique index on <c>CacheKey</c> (needed by <c>INSERT OR REPLACE</c>),
    /// an index on <c>ExpiresAt</c> for eviction, a composite key-and-expiry index for lookups, and column types.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicationCacheLayerEntity>().HasNoKey();
        modelBuilder.Entity<ApplicationCacheLayerEntity>(entity =>
        {
            //entity.HasKey(e => e.Id);

            entity.HasIndex(x => x.CacheKey).IsUnique();
            entity.HasIndex(x => x.ExpiresAt);
            entity.HasIndex(x => new { x.CacheKey, x.ExpiresAt }, "IX_CacheKey_ExpiresAt");

            entity.Property(e => e.CacheKey).HasMaxLength(100).IsRequired();
            entity.Property(e => e.CacheValue).HasColumnType("blob").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("integer").IsRequired();
            entity.Property(e => e.ExpiresAt).HasColumnType("integer").IsRequired();
        });
    }

}
