using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using WebHook.Core.Entities;
using WebHook.Core.Entities.CustomEntities;
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
    public DbSet<TopFiveEventAnalyticsEntity> TopFiveEventAnalyticsEntities { get; set; }
    public DbSet<EventCatalogReportingAnalyticsEntity> EventCatalogReportingAnalyticsEntities { get; set; }
    public DbSet<AdminDashboardSummaryReportingAnalyticsEntity> AdminDashboardSummaryReportingAnalyticsEntities { get; set; }

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

        modelBuilder.Entity<TopFiveEventAnalyticsEntity>().HasNoKey().ToView(@"vw_GetMostRecentEvents".ToLower());
        modelBuilder.Entity<EventCatalogReportingAnalyticsEntity>().HasNoKey().ToView(@"vw_GetEventCatalogAnalytics");
        modelBuilder.Entity<AdminDashboardSummaryReportingAnalyticsEntity>().HasNoKey().ToView(@"vw_AdminDashboardSummary");
        modelBuilder.Entity<UserDashboardSummaryReportAnalyticsEntity>().HasNoKey();


        modelBuilder.HasDbFunction(() => GetUserDashboardSummary(default, default, default))
            .HasName("get_user_dashboard_summary");
    }

    public IQueryable<UserDashboardSummaryReportAnalyticsEntity> GetUserDashboardSummary(Guid userId, DateOnly? startDate, DateOnly? endDate)
            => FromExpression(() => GetUserDashboardSummary(userId, startDate, endDate));
}
