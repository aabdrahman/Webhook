using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebHook.Core.Entities;
using WebHook.Infrastructure.Data_Persistence.CustomDbColumnConverters;

namespace WebHook.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="User"/>.
/// </summary>
/// <remarks>
/// A global query filter hides inactive users, which also affects ASP.NET Core Identity
/// lookups such as <c>FindByEmailAsync</c>. Use <c>IgnoreQueryFilters()</c> to reach them.
/// </remarks>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.CreatedAt, "IX_User_CreatedAt");

        builder.Property(x => x.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasConversion<DateTimeOffsetColumnConverter>();

        builder.Property(x => x.DeletedAt)
            .IsRequired(false)
            .HasConversion<NullableDateTimeOffsetColumnConverter>();

        builder.Property(x => x.RefreshToken)
            .IsRequired(false);

        builder.Property(x => x.LastAuthenticatedAt)
            .HasConversion<NullableDateTimeOffsetColumnConverter>()
            .IsRequired(false);

        builder.Property(x => x.LastLoginDate)
            .HasConversion<NullableDateTimeOffsetColumnConverter>()
            .IsRequired(false);

        builder.Property(x => x.TokenExpirationTime)
            .IsRequired(false)
            .HasConversion<NullableDateTimeOffsetColumnConverter>();

        builder.Property(x => x.FirstName)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.LastName)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.DeletedByUserId)
            .IsRequired(false);

        builder.Property(x => x.DeactivationJustification)
            .IsRequired(false)
            .HasMaxLength(500);

        builder.HasQueryFilter(x => x.IsActive);

        //Realtonship with subscriptions
        builder.HasMany(x => x.WebhookSubscriptions)
            .WithOne(x => x.CreatedByUser)
            .HasForeignKey(x => x.CreatedByUserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
