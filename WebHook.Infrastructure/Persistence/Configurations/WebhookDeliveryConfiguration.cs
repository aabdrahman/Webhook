using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebHook.Core.Entities;

namespace WebHook.Infrastructure.Data_Persistence.Configurations;

internal sealed class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> builder)
    {
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.WebhookSubscriptionEventId);

        builder.HasIndex(x => x.DeliveryStatus);

        builder.ToTable(wd => wd.HasCheckConstraint("CK_WebhookDelivery_Status", "\"DeliveryStatus\" IN ('Pending', 'Processing', 'Delivered', 'Failed', 'Retrying', 'DeadLetter')"));

        builder.ToTable(wd => wd.HasCheckConstraint("CK_WebhookDelivery_RetryCount", "\"RetryCount\" >= 0"));

        builder.ToTable(wd => wd.HasCheckConstraint("CK_WebhookDelivery_DeliveredSttatus", "\"DeliveryStatus\" != 'Delivered' OR \"DeliveredAt\" IS NOT NULL"));

        builder.Property(x => x.RequestPayload)
            .IsRequired();

        builder.Property(x => x.RetryCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(x => x.LockedUntil)
            .IsRequired(false);

        builder.Property(x => x.LockedBy)
            .IsRequired(false)
            .HasMaxLength(100);


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

        //builder.HasOne(x => x.webHookEventCatalog)
        //    .WithMany(x => x.WebhookDeliveries)
        //    .HasForeignKey(x => x.WebhookEventCatalogId)
        //    .OnDelete(DeleteBehavior.NoAction);

        //builder.HasOne(x => x.webhookSubscription)
        //    .WithMany(x => x.WebhookDeliveries)
        //    .HasForeignKey(x => x.WebhookSubscriptionId)
        //    .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.WebhookSubscriptionEvent)
            .WithMany(x => x.WebhookDeliveries)
            .HasForeignKey(x => x.WebhookSubscriptionEventId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.webhookEvent)
            .WithMany(x => x.WebhookDeliveries)
            .HasForeignKey(x => x.WebhookEventId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
