using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using WebHook.Core.Entities;
using WebHook.Core.Entities.CustomEntities;
using WebHook.Infrastructure.Data_Persistence.CustomDbColumnConverters;

namespace WebHook.Infrastructure.Data_Persistence;

/// <summary>
/// The application's EF Core database context for PostgreSQL, including the ASP.NET Core Identity tables.
/// </summary>
/// <remarks>
/// Entity mappings are discovered from the <c>IEntityTypeConfiguration</c> classes in this assembly.
/// Several entities have global query filters that hide inactive or removed rows; see their
/// configuration classes.
/// </remarks>
public class RepositoryContext : IdentityDbContext<User, Role, Guid>
{
    /// <summary>The event catalog: the event types that can be published and subscribed to.</summary>
    public DbSet<WebHookEventCatalog> WebHookEventCatalogs { get; set; }
    /// <summary>Dead-letter entries for deliveries that exhausted their retries.</summary>
    public DbSet<WebhookDeadLetterQueue> WebhookDeadLetterQueues { get; set; }
    /// <summary>Deliveries: one event to one subscription.</summary>
    public DbSet<WebhookDelivery> WebhookDeliveries { get; set; }
    /// <summary>Raised events.</summary>
    public DbSet<WebhookEvent> WebhookEvents { get; set; }
    /// <summary>The HTTP attempts made for deliveries.</summary>
    public DbSet<WebhookDeliveryAttempt> WebhookDeliveryAttempts { get; set; }
    /// <summary>Links between subscriptions and the event types they receive.</summary>
    public DbSet<WebhookSubscriptionEvent> WebhookEventSubscriptions { get; set; }
    /// <summary>Webhook subscriptions.</summary>
    public DbSet<WebhookSubscription> WebhookSubscriptions { get; set; }
    /// <summary>Operation tokens issued after OTP verification.</summary>
    public DbSet<OtpOperationToken> OtpOperationTokens { get; set; }
    /// <summary>Issued one-time passwords.</summary>
    public DbSet<OtpVerification> OtpVerifications { get; set; }
    /// <summary>Onboarded service clients.</summary>
    public DbSet<WebhookServiceClient> WebhookServiceClients { get; set; }
    /// <summary>The event types each service client may publish.</summary>
    public DbSet<WebhookServiceClientEventCatalog> WebhookServiceClientEventCatalogs { get; set; }
    /// <summary>Read-only rows from the <c>vw_getmostrecentevents</c> view.</summary>
    public DbSet<TopFiveEventAnalyticsEntity> TopFiveEventAnalyticsEntities { get; set; }
    /// <summary>Read-only rows from the <c>vw_GetEventCatalogAnalytics</c> view.</summary>
    public DbSet<EventCatalogReportingAnalyticsEntity> EventCatalogReportingAnalyticsEntities { get; set; }
    /// <summary>Read-only row from the <c>vw_AdminDashboardSummary</c> view.</summary>
    public DbSet<AdminDashboardSummaryReportingAnalyticsEntity> AdminDashboardSummaryReportingAnalyticsEntities { get; set; }

    /// <summary>
    /// Initialises a new instance of the <see cref="RepositoryContext"/> class.
    /// </summary>
    /// <param name="options">The context options, including the PostgreSQL connection.</param>
    public RepositoryContext(DbContextOptions<RepositoryContext> options) : base(options)
    {
    }

    /// <summary>
    /// Applies <see cref="CustomDbColumnConverters.DateTimeOffsetColumnConverter"/> and its nullable
    /// counterpart to every <see cref="DateTimeOffset"/> property, so values are stored in UTC.
    /// </summary>
    /// <param name="configurationBuilder">The model configuration builder.</param>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetColumnConverter>();

        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<NullableDateTimeOffsetColumnConverter>();

    }

    /// <summary>
    /// Applies the entity configurations in this assembly and maps the keyless analytics
    /// entities to their views and the <c>get_user_dashboard_summary</c> function.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
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

    /// <summary>
    /// Calls the <c>get_user_dashboard_summary</c> table-valued database function.
    /// </summary>
    /// <param name="userId">The user whose subscriptions to summarise.</param>
    /// <param name="startDate">The inclusive start of the range for the time-bound counts.</param>
    /// <param name="endDate">The exclusive end of the range for the time-bound counts.</param>
    /// <returns>A query returning a single summary row.</returns>
    public IQueryable<UserDashboardSummaryReportAnalyticsEntity> GetUserDashboardSummary(Guid userId, DateOnly? startDate, DateOnly? endDate)
            => FromExpression(() => GetUserDashboardSummary(userId, startDate, endDate));
}
