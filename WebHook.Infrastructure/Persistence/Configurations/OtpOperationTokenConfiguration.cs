using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebHook.Core.Entities;
using WebHook.Infrastructure.Data_Persistence.CustomDbColumnConverters;

namespace WebHook.Infrastructure.Persistence.Configurations;

public class OtpOperationTokenConfiguration : IEntityTypeConfiguration<OtpOperationToken>
{
    public void Configure(EntityTypeBuilder<OtpOperationToken> builder)
    {
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.CreatedAt);

        builder.HasIndex(x => x.ExpiresAt);

        builder.HasIndex(x => x.UserId);

        builder.HasIndex(x => x.OtpVerificationId);

        builder.HasIndex(x => x.Jti).IsUnique();

        builder.Property(x => x.Jti)
            .IsRequired(true);

        builder.Property(x => x.Purpose)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(x => x.TokenHash)
            .IsRequired(false);

        builder.Property(x => x.CreatedAt)
            .HasConversion<DateTimeOffsetColumnConverter>()
            .IsRequired();

        builder.Property(x => x.ExpiresAt)
            .HasConversion<DateTimeOffsetColumnConverter>()
            .IsRequired();

        builder.Property(x => x.ConsumedAt)
            .HasConversion<NullableDateTimeOffsetColumnConverter>()
            .IsRequired(false);

        builder.Property(x => x.RevokedAt)
            .HasConversion<NullableDateTimeOffsetColumnConverter>()
            .IsRequired(false);

        builder.Property(x => x.UserId)
            .IsRequired(false);

        builder.HasOne(x => x.OtpVerification)
            .WithMany()
            .HasForeignKey(x => x.OtpVerificationId)
            .OnDelete(DeleteBehavior.ClientCascade)
            .IsRequired();

        builder.HasOne(x => x.UserToPerformOperation)
            .WithMany(x => x.OtpOperationTokens)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.ClientNoAction)
            .IsRequired(false);

    }
}