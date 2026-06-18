using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebHook.Core.Entities;
using WebHook.Infrastructure.Data_Persistence.CustomDbColumnConverters;

namespace WebHook.Infrastructure.Data_Persistence.Configurations;

public sealed class WebhookEventCatalogConfiguration : IEntityTypeConfiguration<WebHookEventCatalog>
{
    public void Configure(EntityTypeBuilder<WebHookEventCatalog> builder)
    {
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.NormalizedEventName)
            .IsUnique();

        builder.Property(x => x.Description)
            .HasMaxLength(150)
            .IsRequired(false);

        builder.Property(x => x.EventName)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.NormalizedEventName)
            //.HasComputedColumnSql("UPPER(EventName)", stored: true) This works for sql server but fails for postgres
            .HasComputedColumnSql("UPPER(\"EventName\")", stored: true)
            .IsRequired();

        builder.Property(x => x.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(x => x.AvailableFields)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasConversion<DateTimeOffsetColumnConverter>();

        //RELATIONSHIPS
        builder.HasMany(x => x.WebhookSubscriptions)
            .WithOne(x => x.webHookEventCatalog)
            .HasForeignKey(x => x.WebhookEventCatalogId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasMany(x => x.WebhookDeliveries)
            .WithOne(x => x.webHookEventCatalog)
            .HasForeignKey(x => x.WebhookEventCatalogId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
