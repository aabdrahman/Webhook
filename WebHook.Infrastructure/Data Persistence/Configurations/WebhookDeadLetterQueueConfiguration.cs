using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebHook.Core.Entities;

namespace WebHook.Infrastructure.Data_Persistence.Configurations;

internal sealed class WebhookDeadLetterQueueConfiguration : IEntityTypeConfiguration<WebhookDeadLetterQueue>
{
    public void Configure(EntityTypeBuilder<WebhookDeadLetterQueue> builder)
    {
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.WebhookDeliveryId, "IX_Webhook_DeadLetter_DeliveryId");

        builder.Property(x => x.Reason)
            .IsRequired()
            .HasMaxLength(250);

        builder.HasOne(x => x.webhookDelivery)
            .WithMany(x => x.webhookDeadLetterQueues)
            .HasForeignKey(x => x.WebhookDeliveryId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}