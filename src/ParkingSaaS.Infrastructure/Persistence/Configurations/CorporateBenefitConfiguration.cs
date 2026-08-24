using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParkingSaaS.Domain.Benefits;

namespace ParkingSaaS.Infrastructure.Persistence.Configurations;

public sealed class CorporateBenefitProgramConfiguration : IEntityTypeConfiguration<CorporateBenefitProgram>
{
    public void Configure(EntityTypeBuilder<CorporateBenefitProgram> b)
    {
        b.ToTable("corporate_benefit_programs");
        b.HasKey(p => p.Id);
        b.Property(p => p.TenantId).IsRequired();
        b.Property(p => p.Name).HasMaxLength(160).IsRequired();
        b.Property(p => p.Description).HasMaxLength(500).IsRequired();
        b.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(p => p.Priority).IsRequired();

        b.HasIndex(p => new { p.TenantId, p.Status });
        b.HasIndex(p => new { p.TenantId, p.Priority });
    }
}

public sealed class CorporateBenefitProgramLocationConfiguration : IEntityTypeConfiguration<CorporateBenefitProgramLocation>
{
    public void Configure(EntityTypeBuilder<CorporateBenefitProgramLocation> b)
    {
        b.ToTable("corporate_benefit_program_locations");
        b.HasKey(p => p.Id);
        b.Property(p => p.TenantId).IsRequired();
        b.Property(p => p.CorporateBenefitProgramId).IsRequired();
        b.Property(p => p.ParkingLocationId).IsRequired();
        b.Property(p => p.MaxConcurrentFreeSessions).IsRequired();

        b.HasIndex(p => new { p.TenantId, p.CorporateBenefitProgramId, p.ParkingLocationId }).IsUnique();
        b.HasIndex(p => new { p.TenantId, p.ParkingLocationId });
        b.HasOne<CorporateBenefitProgram>().WithMany().HasForeignKey(p => p.CorporateBenefitProgramId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ParkingSaaS.Domain.Locations.ParkingLocation>().WithMany().HasForeignKey(p => p.ParkingLocationId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CorporateBenefitProgramVersionConfiguration : IEntityTypeConfiguration<CorporateBenefitProgramVersion>
{
    public void Configure(EntityTypeBuilder<CorporateBenefitProgramVersion> b)
    {
        b.ToTable("corporate_benefit_program_versions");
        b.HasKey(v => v.Id);
        b.Property(v => v.TenantId).IsRequired();
        b.Property(v => v.CorporateBenefitProgramId).IsRequired();
        b.Property(v => v.VersionNumber).IsRequired();
        b.Property(v => v.RulesJson).HasColumnType("jsonb").IsRequired();
        b.Property(v => v.CreatedByUserId).IsRequired();
        b.Property(v => v.CreatedAt).IsRequired();

        b.HasIndex(v => new { v.CorporateBenefitProgramId, v.VersionNumber }).IsUnique();
        b.HasIndex(v => new { v.CorporateBenefitProgramId, v.EffectiveFrom, v.EffectiveTo });
        b.HasOne<CorporateBenefitProgram>().WithMany().HasForeignKey(v => v.CorporateBenefitProgramId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CorporateBenefitPlateConfiguration : IEntityTypeConfiguration<CorporateBenefitPlate>
{
    public void Configure(EntityTypeBuilder<CorporateBenefitPlate> b)
    {
        b.ToTable("corporate_benefit_plates");
        b.HasKey(p => p.Id);
        b.Property(p => p.TenantId).IsRequired();
        b.Property(p => p.CorporateBenefitProgramId).IsRequired();
        b.Property(p => p.PlateNumberNormalized).HasMaxLength(32).IsRequired();
        b.Property(p => p.PlateNumberDisplay).HasMaxLength(32).IsRequired();
        b.Property(p => p.IsActive).IsRequired();
        b.HasIndex(p => new { p.TenantId, p.CorporateBenefitProgramId, p.PlateNumberNormalized }).IsUnique();
        b.HasIndex(p => new { p.TenantId, p.PlateNumberNormalized, p.IsActive });
        b.HasOne<CorporateBenefitProgram>().WithMany().HasForeignKey(p => p.CorporateBenefitProgramId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CorporateBenefitAllocationConfiguration : IEntityTypeConfiguration<CorporateBenefitAllocation>
{
    public void Configure(EntityTypeBuilder<CorporateBenefitAllocation> b)
    {
        b.ToTable("corporate_benefit_allocations");
        b.HasKey(a => a.Id);
        b.Property(a => a.TenantId).IsRequired();
        b.Property(a => a.CorporateBenefitProgramId).IsRequired();
        b.Property(a => a.CorporateBenefitProgramVersionId).IsRequired();
        b.Property(a => a.ParkingLocationId).IsRequired();
        b.Property(a => a.ParkingSessionId).IsRequired();
        b.Property(a => a.PlateNumberNormalized).HasMaxLength(32).IsRequired();
        b.Property(a => a.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        b.HasIndex(a => new { a.TenantId, a.ParkingSessionId }).IsUnique();
        b.HasIndex(a => new { a.TenantId, a.CorporateBenefitProgramId, a.ParkingLocationId, a.Status });
        b.HasIndex(a => new { a.ParkingLocationId, a.Status });
        b.HasOne<CorporateBenefitProgram>().WithMany().HasForeignKey(a => a.CorporateBenefitProgramId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CorporateBenefitProgramVersion>().WithMany().HasForeignKey(a => a.CorporateBenefitProgramVersionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ParkingSaaS.Domain.Locations.ParkingLocation>().WithMany().HasForeignKey(a => a.ParkingLocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ParkingSaaS.Domain.Sessions.ParkingSession>().WithMany().HasForeignKey(a => a.ParkingSessionId).OnDelete(DeleteBehavior.Cascade);
    }
}
