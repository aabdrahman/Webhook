using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebHook.Core.Entities;

namespace WebHook.Infrastructure.Data_Persistence.Configurations;

public sealed class WebhookSubscriptionConfiguration : IEntityTypeConfiguration<WebhookSubscription>
{
    public void Configure(EntityTypeBuilder<WebhookSubscription> builder)
    {
        builder.HasKey(x => x.Id);

        builder.HasQueryFilter(x => x.IsActive);

        builder.Property(x => x.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.CallbackUrl)
            .IsRequired();

        builder.Property(x => x.IsActive)
            .IsRequired();

        builder.Property(x => x.SecretKey)
            .IsRequired();

        builder.Property(x => x.SubscribedFields)
            .HasColumnType("jsonb");

        //RELATIONSHIPS
        builder.HasMany(x => x.WebhookEvents)
            .WithOne(x => x.webhookSubscription)
            .HasForeignKey(x => x.WebhookSubscriptionId)
            .OnDelete(DeleteBehavior.NoAction);

        //builder.HasMany(x => x.WebhookDeliveries)
        //    .WithOne(x => x.webhookSubscription)
        //    .HasForeignKey(x => x.WebhookSubscriptionId)
        //    .OnDelete(DeleteBehavior.NoAction);
    }
}
