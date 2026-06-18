using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebHook.Core.Entities;

namespace WebHook.Infrastructure.Data_Persistence.Configurations;

internal sealed class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.RequestPayload)
            .IsRequired();

        builder.Property(x => x.RetryCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(x => x.DeliveryStatus)
            .HasConversion<string>()
            .IsRequired();

        //Relationships
        builder.HasMany(x => x.WebhookDeliveryAttempts)
            .WithOne(x => x.webhookDelivery)
            .HasForeignKey(x => x.WebhookDeliveryId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasMany(x => x.webhookDeadLetterQueues)
            .WithOne(x => x.webhookDelivery)
            .HasForeignKey(x => x.WebhookDeliveryId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.webHookEventCatalog)
            .WithMany(x => x.WebhookDeliveries)
            .HasForeignKey(x => x.WebhookEventCatalogId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.webhookSubscription)
            .WithMany(x => x.WebhookDeliveries)
            .HasForeignKey(x => x.WebhookSubscriptionId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.webhookEvent)
            .WithMany(x => x.WebhookDeliveries)
            .HasForeignKey(x => x.WebhookEventId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
