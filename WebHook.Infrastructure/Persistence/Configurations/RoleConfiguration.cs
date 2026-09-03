using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebHook.Core.Entities;

namespace WebHook.Infrastructure.Persistence.Configurations;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.Property(x => x.CreatedAt)
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .ValueGeneratedOnAdd()
            .IsRequired(false);

        builder.Property(x => x.Description)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(x => x.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.HasData(new List<Role>()
        {
            new Role()
            {
                Id = new Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890"),
                Name = "Admin",
                NormalizedName = "ADMIN",
                Description = "Administrator role with full access",
                IsActive = true,
                ConcurrencyStamp = "ec511bd4-4853-426a-a2fc-751886560c9a"
            },
            new Role()
            {
                Id = new Guid("b2c3d4e5-f6a7-8901-bcde-f12345678901"),
                Name = "User",
                NormalizedName = "USER",
                Description = "Regular user role with limited access",
                IsActive = true,
                ConcurrencyStamp = "d1f2e3c4-b5a6-7890-cdef-123456789012"
            },
            new Role()
            {
                Id = new Guid("c3d4e5f6-a7b8-9012-cdef-123456789012"),
                Name = "System",
                NormalizedName = "SYSTEM",
                Description = "System role with administrative privileges",
                IsActive = true,
                ConcurrencyStamp = "f1e2d3c4-b5a6-7890-cdef-123456789012"
            }
        });
    }
}