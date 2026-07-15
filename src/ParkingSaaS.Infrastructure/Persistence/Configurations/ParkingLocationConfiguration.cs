using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParkingSaaS.Domain.Locations;

namespace ParkingSaaS.Infrastructure.Persistence.Configurations;

public sealed class ParkingLocationConfiguration : IEntityTypeConfiguration<ParkingLocation>
{
    public void Configure(EntityTypeBuilder<ParkingLocation> b)
    {
        b.ToTable("parking_locations");
        b.HasKey(l => l.Id);
        b.Property(l => l.TenantId).IsRequired();
        b.Property(l => l.Name).HasMaxLength(200).IsRequired();
        b.Property(l => l.Slug).HasMaxLength(80).IsRequired();
        b.Property(l => l.Address).HasMaxLength(500);
        b.Property(l => l.Timezone).HasMaxLength(64).IsRequired();
        b.Property(l => l.Status).HasConversion<string>().HasMaxLength(32);
        b.Property(l => l.PublicQrCodeUrl).HasMaxLength(500);

        // Slug is unique per platform so public location routing is unambiguous.
        b.HasIndex(l => l.Slug).IsUnique();
        b.HasIndex(l => l.TenantId);
        b.HasIndex(l => new { l.TenantId, l.Status });
    }
}
