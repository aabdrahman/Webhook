using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebHook.Core.Entities;
using WebHook.Infrastructure.Data_Persistence.CustomDbColumnConverters;

namespace WebHook.Infrastructure.Persistence.Configurations;

public sealed class WebhookServiceClientEventCatalogConfiguration : IEntityTypeConfiguration<WebhookServiceClientEventCatalog>
{
    public void Configure(EntityTypeBuilder<WebhookServiceClientEventCatalog> builder)
    {
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.ServiceClientId, "IX_ServiceClient_ServiceCleintId");

        builder.HasIndex(x => x.EventCatalogId, "IX_ServiceClient_CatalogId");

        builder.HasIndex(x => new { x.EventCatalogId, x.ServiceClientId }).IsUnique();

        builder.HasIndex(x => x.DeactivatedAt);

        builder.HasIndex(x => x.CreatedAt, "IX_CreatedAt");

        builder.HasQueryFilter(x => !x.DeactivatedAt.HasValue);

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasConversion<DateTimeOffsetColumnConverter>();

        builder.Property(x => x.DeactivatedAt)
            .IsRequired(false)
            .HasConversion<NullableDateTimeOffsetColumnConverter>();

        builder.Property(x => x.DeactivatedBy)
            .IsRequired(false);

        builder.HasOne(x => x.eventCatalog)
            .WithMany(x => x.WebhookServiceClients)
            .HasForeignKey(x => x.EventCatalogId)
            .OnDelete(DeleteBehavior.ClientCascade);

        builder.HasOne(x => x.serviceClient)
            .WithMany(x => x.EventCatalogs)
            .HasForeignKey(x => x.ServiceClientId)
            .OnDelete(DeleteBehavior.ClientCascade);
    }
}