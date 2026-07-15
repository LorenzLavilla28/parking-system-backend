using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParkingSaaS.Domain.Users;

namespace ParkingSaaS.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> b)
    {
        b.ToTable("users");
        b.HasKey(u => u.Id);
        b.Property(u => u.TenantId).IsRequired();
        b.Property(u => u.FirstName).HasMaxLength(100).IsRequired();
        b.Property(u => u.LastName).HasMaxLength(100).IsRequired();
        b.Property(u => u.Email).HasMaxLength(256).IsRequired();
        b.Property(u => u.PasswordHash).IsRequired();
        b.Property(u => u.Status).HasConversion<string>().HasMaxLength(32);

        // Email is globally unique across the platform (used for tenant-agnostic login).
        b.HasIndex(u => u.Email).IsUnique();
        b.HasIndex(u => new { u.TenantId, u.Email });

        b.HasMany(u => u.Roles)
            .WithOne()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(u => u.LocationAssignments)
            .WithOne()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Navigation(u => u.Roles).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(u => u.LocationAssignments).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> b)
    {
        b.ToTable("user_roles");
        b.HasKey(r => r.Id);
        b.Property(r => r.Role).HasConversion<string>().HasMaxLength(32);
        b.HasIndex(r => new { r.UserId, r.Role }).IsUnique();
    }
}

public sealed class UserParkingLocationConfiguration : IEntityTypeConfiguration<UserParkingLocation>
{
    public void Configure(EntityTypeBuilder<UserParkingLocation> b)
    {
        b.ToTable("user_parking_locations");
        b.HasKey(a => a.Id);
        b.HasIndex(a => new { a.UserId, a.ParkingLocationId }).IsUnique();
        b.HasIndex(a => new { a.TenantId, a.ParkingLocationId });
    }
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens");
        b.HasKey(t => t.Id);
        b.Property(t => t.TokenHash).HasMaxLength(128).IsRequired();
        b.HasIndex(t => t.TokenHash).IsUnique();
        b.HasIndex(t => new { t.UserId, t.ExpiresAt });
        b.Property(t => t.CreatedByIp).HasMaxLength(64);
        b.Property(t => t.ReplacedByTokenHash).HasMaxLength(128);
    }
}

public sealed class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> b)
    {
        b.ToTable("password_reset_tokens");
        b.HasKey(t => t.Id);
        b.Property(t => t.TokenHash).HasMaxLength(128).IsRequired();
        b.HasIndex(t => t.TokenHash).IsUnique();
        b.HasIndex(t => new { t.UserId, t.ExpiresAt });
        b.HasIndex(t => new { t.TenantId, t.ExpiresAt });
    }
}
