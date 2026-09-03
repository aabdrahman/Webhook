using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using WebHook.Core.Entities;
using WebHook.Infrastructure.Data_Persistence.CustomDbColumnConverters;

namespace WebHook.Infrastructure.Data_Persistence;

public class RepositoryContext : IdentityDbContext<User, Role, Guid>
{
    public DbSet<WebHookEventCatalog> WebHookEventCatalogs { get; set; }
    public DbSet<WebhookDeadLetterQueue> WebhookDeadLetterQueues { get; set; }
    public DbSet<WebhookDelivery> WebhookDeliveries { get; set; }
    public DbSet<WebhookEvent> WebhookEvents { get; set; }
    public DbSet<WebhookDeliveryAttempt> WebhookDeliveryAttempts { get; set; }
    public DbSet<WebhookSubscriptionEvent> WebhookEventSubscriptions { get; set; }
    public DbSet<WebhookSubscription> WebhookSubscriptions { get; set; }
    public DbSet<OtpOperationToken> OtpOperationTokens { get; set; }
    public DbSet<OtpVerification> OtpVerifications { get; set; }
    public DbSet<WebhookServiceClient> WebhookServiceClients { get; set; }
    public DbSet<WebhookServiceClientEventCatalog> WebhookServiceClientEventCatalogs { get; set; }

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
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(assembly: Assembly.GetExecutingAssembly());
    }
}
