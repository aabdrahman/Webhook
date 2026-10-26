using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebHook.Core.Entities;

namespace WebHook.Infrastructure.Data_Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="WebhookSubscription"/>. <c>SubscribedFields</c> is stored as
/// <c>jsonb</c>, and a global query filter hides inactive subscriptions.
/// </summary>
public sealed class WebhookSubscriptionConfiguration : IEntityTypeConfiguration<WebhookSubscription>
{
    /// <inheritdoc />
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

        builder.Property(x => x.ContactEmail)
            .IsRequired(false)
            .HasMaxLength(100);

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
