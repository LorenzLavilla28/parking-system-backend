using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParkingSaaS.Domain.Tenants;

namespace ParkingSaaS.Infrastructure.Persistence.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.ToTable("tenants");
        b.HasKey(t => t.Id);
        b.Property(t => t.Name).HasMaxLength(200).IsRequired();
        b.Property(t => t.Slug).HasMaxLength(80).IsRequired();
        b.HasIndex(t => t.Slug).IsUnique();
        b.Property(t => t.Status).HasConversion<string>().HasMaxLength(32);
        b.Property(t => t.SubscriptionPlan).HasConversion<string>().HasMaxLength(32);
        b.Property(t => t.PurchasedSlotCapacityPerLocation);
        b.Property(t => t.CapacityPricingEnabled).IsRequired();
        b.Property(t => t.AdditionalSlotCapacity).IsRequired();
        b.Property(t => t.DefaultCurrency).HasMaxLength(3).IsRequired();
        b.Property(t => t.DefaultTimezone).HasMaxLength(64).IsRequired();
        b.Property(t => t.LogoObjectKey).HasMaxLength(300);
        b.Property(t => t.LogoContentType).HasMaxLength(64);
    }
}
