using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebHook.Core.Entities;
using WebHook.Infrastructure.Data_Persistence.CustomDbColumnConverters;

namespace WebHook.Infrastructure.Persistence.Configurations;

public sealed class WebhookServiceClientConfiguration : IEntityTypeConfiguration<WebhookServiceClient>
{
    public void Configure(EntityTypeBuilder<WebhookServiceClient> builder)
    {
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.ClientId).IsUnique();

        builder.HasIndex(x => x.CreatedAt);

        builder.HasIndex(x => x.ServiceClientName);

        builder.HasQueryFilter(x => x.IsActive);

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasConversion<DateTimeOffsetColumnConverter>();

        builder.Property(x => x.ClientId)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.ClientKey)
            .IsRequired();

        builder.Property(x => x.ServiceClientName)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(x => x.DeactivatedAt)
            .IsRequired(false)
            .HasConversion<NullableDateTimeOffsetColumnConverter>();

        builder.Property(x => x.DeactivatedBy)
            .IsRequired(false);

        builder.HasMany(x => x.EventCatalogs)
            .WithOne(x => x.serviceClient)
            .HasForeignKey(x => x.ServiceClientId)
            .OnDelete(DeleteBehavior.ClientCascade);
    }
}
