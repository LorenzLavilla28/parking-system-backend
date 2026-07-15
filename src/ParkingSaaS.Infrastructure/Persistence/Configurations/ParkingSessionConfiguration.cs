using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParkingSaaS.Domain.Sessions;

namespace ParkingSaaS.Infrastructure.Persistence.Configurations;

public sealed class ParkingSessionConfiguration : IEntityTypeConfiguration<ParkingSession>
{
    // Status values considered "active" — must match ParkingSessionStatusExtensions.ActiveStates.
    private const string ActiveStatusFilter =
        "\"Status\" IN ('ActiveUnpaid','PaymentPending','PaidExitWindow','OverstayDue')";

    public void Configure(EntityTypeBuilder<ParkingSession> b)
    {
        b.ToTable("parking_sessions");
        b.HasKey(s => s.Id);

        b.Property(s => s.TenantId).IsRequired();
        b.Property(s => s.ParkingLocationId).IsRequired();
        b.Property(s => s.PlateNumberRaw).HasMaxLength(32).IsRequired();
        b.Property(s => s.PlateNumberNormalized).HasMaxLength(32).IsRequired();
        b.Property(s => s.VehicleType).HasConversion<string>().HasMaxLength(20);
        b.Property(s => s.VehicleColor).HasMaxLength(40);
        b.Property(s => s.Status).HasConversion<string>().HasMaxLength(32);

        b.Property(s => s.PublicTokenHash).HasMaxLength(64).IsRequired();
        b.Property(s => s.PublicTokenProtected).IsRequired();
        b.Property(s => s.TicketCodeHash).HasMaxLength(64).IsRequired();
        b.Property(s => s.TicketCodeProtected).IsRequired();

        b.Property(s => s.EntryPhotoUrl).HasMaxLength(1000);
        b.Property(s => s.ExitPhotoUrl).HasMaxLength(1000);

        // Money: never floating point.
        b.Property(s => s.FinalFee).HasColumnType("numeric(12,2)");
        b.Property(s => s.TotalPaid).HasColumnType("numeric(12,2)");
        b.Property(s => s.FeeOverride).HasColumnType("numeric(12,2)");

        // Optimistic concurrency via PostgreSQL's xmin system column.
        b.Property(s => s.ConcurrencyToken)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        // Lookup indexes (per the database requirements).
        b.HasIndex(s => s.TenantId);
        b.HasIndex(s => new { s.TenantId, s.ParkingLocationId });
        b.HasIndex(s => new { s.TenantId, s.PlateNumberNormalized });
        b.HasIndex(s => new { s.ParkingLocationId, s.Status, s.PlateNumberNormalized });
        b.HasIndex(s => s.PublicTokenHash).IsUnique();
        b.HasIndex(s => new { s.ParkingLocationId, s.TicketCodeHash });
        b.HasIndex(s => s.EntryTime);
        b.HasIndex(s => s.ExitTime);

        // At most one ACTIVE session per (location, normalized plate). A partial
        // unique index lets the same plate re-enter after a prior session closes.
        b.HasIndex(s => new { s.ParkingLocationId, s.PlateNumberNormalized })
            .HasFilter(ActiveStatusFilter)
            .IsUnique()
            .HasDatabaseName("ux_active_session_per_plate_location");
    }
}
