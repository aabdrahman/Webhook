using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebHook.Core.Entities;
using WebHook.Infrastructure.Data_Persistence.CustomDbColumnConverters;

namespace WebHook.Infrastructure.Persistence.Configurations;

internal sealed class OtpVerificationConfiguration : IEntityTypeConfiguration<OtpVerification>
{
    public void Configure(EntityTypeBuilder<OtpVerification> builder)
    {
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.CreatedAt);

        builder.HasIndex(x => x.ExpiresAt);

        builder.HasIndex(x => x.UserId);

        builder.Property(x => x.UserId)
            .IsRequired(false);

        builder.Property(x => x.Purpose)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(x => x.OtpHash)
            .IsRequired(false);

        builder.Property(x => x.RevokedAt)
            .HasConversion<NullableDateTimeOffsetColumnConverter>()
            .IsRequired(false);

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasConversion<DateTimeOffsetColumnConverter>();

        builder.Property(x => x.ExpiresAt)
            .IsRequired()
            .HasConversion<DateTimeOffsetColumnConverter>();

        builder.Property(x => x.ValidatedAt)
            .IsRequired(false)
            .HasConversion<NullableDateTimeOffsetColumnConverter>();

        builder.Property(x => x.ConsumedAt)
            .IsRequired(false)
            .HasConversion<NullableDateTimeOffsetColumnConverter>();

        builder.Property(x => x.IsConsumed)
            .IsRequired(true);

        builder.HasOne(x => x.UserToVerify)
            .WithMany(x => x.OtpVerifications)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.NoAction)
            .IsRequired(false);
    }
}
