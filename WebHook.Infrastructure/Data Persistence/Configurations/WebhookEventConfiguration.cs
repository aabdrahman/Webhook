using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebHook.Core.Entities;

namespace WebHook.Infrastructure.Data_Persistence.Configurations;

internal sealed class WebhookEventConfiguration : IEntityTypeConfiguration<WebhookEvent>
{
    public void Configure(EntityTypeBuilder<WebhookEvent> builder)
    {
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.EventType, "IX_Webhook_Event_EventType");

        builder.HasIndex(x => x.CorrelationId, "IX_Webhook_Event_CorrelationId");

        builder.Property(x => x.EventType)
            .IsRequired();

        builder.Property(x => x.CorrelationId)
            .IsRequired();

        builder.Property(x => x.PayLoad)
            .IsRequired();

        builder.Property(x => x.Source)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .IsRequired();

        //Relationship
        builder.HasMany(x => x.WebhookDeliveries)
            .WithOne(x => x.webhookEvent)
            .HasForeignKey(x => x.WebhookEventId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
