using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParkingSaaS.Domain.RatePlans;

namespace ParkingSaaS.Infrastructure.Persistence.Configurations;

public sealed class RatePlanConfiguration : IEntityTypeConfiguration<RatePlan>
{
    public void Configure(EntityTypeBuilder<RatePlan> b)
    {
        b.ToTable("rate_plans");
        b.HasKey(p => p.Id);
        b.Property(p => p.TenantId).IsRequired();
        b.Property(p => p.ParkingLocationId);
        b.Property(p => p.Name).HasMaxLength(120).IsRequired();
        b.Property(p => p.Description).HasMaxLength(500).IsRequired();
        b.Property(p => p.Status).HasConversion<string>().HasMaxLength(32);

        b.HasIndex(p => new { p.TenantId, p.ParkingLocationId });
        b.HasIndex(p => new { p.ParkingLocationId, p.Status });
    }
}

public sealed class RatePlanVersionConfiguration : IEntityTypeConfiguration<RatePlanVersion>
{
    public void Configure(EntityTypeBuilder<RatePlanVersion> b)
    {
        b.ToTable("rate_plan_versions");
        b.HasKey(v => v.Id);
        b.Property(v => v.TenantId).IsRequired();
        b.Property(v => v.RatePlanId).IsRequired();
        b.Property(v => v.VersionNumber).IsRequired();
        b.Property(v => v.RulesJson).HasColumnType("jsonb").IsRequired();

        b.HasIndex(v => new { v.RatePlanId, v.VersionNumber }).IsUnique();
        // Fast lookup of the in-effect version for a plan.
        b.HasIndex(v => new { v.RatePlanId, v.EffectiveFrom, v.EffectiveTo });
    }
}
