using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebHook.Core.Entities;

namespace WebHook.Infrastructure.Data_Persistence.Configurations;

internal class WebhookDeliveryAttemptConfiguration : IEntityTypeConfiguration<WebhookDeliveryAttempt>
{
    public void Configure(EntityTypeBuilder<WebhookDeliveryAttempt> builder)
    {
        builder.HasKey(x => x.Id);

        builder.ToTable(table => table.HasCheckConstraint("CK_DeliveryAttempt_DurationGreaterThanZero", "\"Duration\" > 0"));

        builder.ToTable(table => table.HasCheckConstraint("CK_DeliveryAttempt_AttemptedCountGreaterThanZero", "\"AttemptedCount\" > 0"));

        builder.Property(x => x.HttpResponse)
            .IsRequired();

        builder.Property(x => x.HttpResponseCode)
            .IsRequired();

        builder.Property(x => x.Duration)
            .IsRequired();

        builder.Property(x => x.AttemptedCount)
            .IsRequired();

        builder.HasOne(x => x.webhookDelivery)
            .WithMany(x => x.WebhookDeliveryAttempts)
            .HasForeignKey(x => x.WebhookDeliveryId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}