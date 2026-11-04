using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebHook.Core.Entities;

namespace WebHook.Infrastructure.Persistence.Configurations;

public class WebhookSubscriptionVerificationConfiguration : IEntityTypeConfiguration<WebhookSubscriptionVerification>
{
    public void Configure(EntityTypeBuilder<WebhookSubscriptionVerification> builder)
    {
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.SubscriptionId);

        builder.HasIndex(x => x.CreatedAt);

        builder.HasIndex(x => x.Status);

        builder.ToTable(x => x.HasCheckConstraint("CK_WebhookSubscriptionVerification_Status", "\"Status\" IN ('Pending', 'Failed', 'Success')"));

        builder.Property(x => x.ChallengeToken)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.ResponseDetails)
            .IsRequired(false);

        builder.Property(x => x.ResponseStatusCode)
            .IsRequired(false)
            .HasMaxLength(10);

        builder.Property(x => x.AttemptedAt)
            .IsRequired(false);

        builder.Property(x => x.ValidatedAt)
            .IsRequired(false);

        builder.HasOne(x => x.Subscription)
            .WithMany(x => x.WebhookSubscriptionVerifications)
            .HasForeignKey(x => x.SubscriptionId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
