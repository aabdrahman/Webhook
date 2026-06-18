using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Reflection;
using WebHook.Core.Entities;
using WebHook.Infrastructure.Data_Persistence.CustomDbColumnConverters;

namespace WebHook.Infrastructure.Data_Persistence;

public class RepositoryContext : DbContext
{
    public DbSet<WebHookEventCatalog> WebHookEventCatalogs { get; set; }
    public DbSet<WebhookDeadLetterQueue> WebhookDeadLetterQueues { get; set; }
    public DbSet<WebhookDelivery> WebhookDeliveries { get; set; }
    public DbSet<WebhookEvent> WebhookEvents { get; set; }
    public DbSet<WebhookDeliveryAttempt> WebhookDeliveryAttempts { get; set; }
    public DbSet<WebhookSubscriptionEvent> WebhookEventSubscriptions { get; set; }
    public DbSet<WebhookSubscription> WebhookSubscriptions { get; set; }

    public RepositoryContext(DbContextOptions<RepositoryContext> options) : base(options)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetColumnConverter>();

        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<NullableDateTimeOffsetColumnConverter>();

    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(assembly: Assembly.GetExecutingAssembly());
    }
}
