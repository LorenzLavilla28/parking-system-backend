using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParkingSaaS.Domain.Audit;

namespace ParkingSaaS.Infrastructure.Persistence.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("audit_logs");
        b.HasKey(a => a.Id);
        b.Property(a => a.TenantId).IsRequired();
        b.Property(a => a.Action).HasMaxLength(80).IsRequired();
        b.Property(a => a.EntityType).HasMaxLength(80).IsRequired();
        b.Property(a => a.EntityId).HasMaxLength(64).IsRequired();
        b.Property(a => a.OldValuesJson).HasColumnType("jsonb");
        b.Property(a => a.NewValuesJson).HasColumnType("jsonb");
        b.Property(a => a.Reason).HasMaxLength(500);
        b.Property(a => a.IpAddress).HasMaxLength(64);
        b.Property(a => a.DeviceInformation).HasMaxLength(256);

        b.HasIndex(a => new { a.TenantId, a.CreatedAt });
        b.HasIndex(a => new { a.TenantId, a.EntityType, a.EntityId });
        b.HasIndex(a => new { a.TenantId, a.ParkingLocationId });
    }
}
