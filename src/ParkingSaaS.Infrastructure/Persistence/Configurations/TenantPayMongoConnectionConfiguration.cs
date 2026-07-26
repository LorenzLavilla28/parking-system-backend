using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParkingSaaS.Domain.Payments;

namespace ParkingSaaS.Infrastructure.Persistence.Configurations;

public sealed class TenantPayMongoConnectionConfiguration : IEntityTypeConfiguration<TenantPayMongoConnection>
{
    public void Configure(EntityTypeBuilder<TenantPayMongoConnection> b)
    {
        b.ToTable("tenant_paymongo_connections");
        b.HasKey(c => c.Id);
        b.Property(c => c.TenantId).IsRequired();
        b.Property(c => c.Environment).HasMaxLength(10).IsRequired();
        b.Property(c => c.PayMongoAccountId).HasMaxLength(128);
        b.Property(c => c.SecretArn).HasMaxLength(512).IsRequired();
        b.Property(c => c.WebhookTokenHash).HasMaxLength(64).IsRequired();
        b.Property(c => c.WebhookTokenProtected).IsRequired();
        b.Property(c => c.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(c => c.LastError).HasMaxLength(500);

        b.HasIndex(c => new { c.TenantId, c.Environment }).IsUnique();
        b.HasIndex(c => c.WebhookTokenHash).IsUnique();
        b.HasIndex(c => c.PayMongoAccountId);
    }
}
