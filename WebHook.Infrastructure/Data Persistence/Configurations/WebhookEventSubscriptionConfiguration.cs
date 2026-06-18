using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebHook.Core.Entities;

namespace WebHook.Infrastructure.Data_Persistence.Configurations;

public sealed class WebhookEventSubscriptionConfiguration : IEntityTypeConfiguration<WebhookSubscriptionEvent>
{
    public void Configure(EntityTypeBuilder<WebhookSubscriptionEvent> builder)
    {
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.WebhookEventCatalogId, x.WebhookSubscriptionId })
            .IsUnique()
            .HasDatabaseName("IX_Webhook_Event_Subscription");

        builder.HasOne(x => x.webHookEventCatalog)
            .WithMany(x => x.WebhookSubscriptions)
            .HasForeignKey(x => x.WebhookSubscriptionId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.webhookSubscription)
            .WithMany(x => x.WebhookEvents)
            .HasForeignKey(x => x.WebhookEventCatalogId)
            .OnDelete(DeleteBehavior.NoAction);
    }

}
