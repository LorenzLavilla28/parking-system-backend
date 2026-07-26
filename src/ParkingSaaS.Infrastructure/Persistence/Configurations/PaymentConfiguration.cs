using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParkingSaaS.Domain.Payments;

namespace ParkingSaaS.Infrastructure.Persistence.Configurations;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("payments");
        b.HasKey(p => p.Id);
        b.Property(p => p.TenantId).IsRequired();
        b.Property(p => p.ParkingSessionId).IsRequired();
        b.Property(p => p.Provider).HasConversion<string>().HasMaxLength(20);
        b.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(p => p.Currency).HasMaxLength(3).IsRequired();
        b.Property(p => p.Amount).HasColumnType("numeric(12,2)");
        b.Property(p => p.PaymentMethod).HasMaxLength(40);
        b.Property(p => p.ProviderCheckoutSessionId).HasMaxLength(128);
        b.Property(p => p.ProviderCheckoutUrl).HasMaxLength(500);
        b.Property(p => p.ProviderPaymentId).HasMaxLength(128);
        b.Property(p => p.ProviderAccountId).HasMaxLength(128);
        b.Property(p => p.PublicReferenceHash).HasMaxLength(64).IsRequired();
        b.Property(p => p.PublicReferenceProtected).IsRequired();
        b.Property(p => p.IdempotencyKey).HasMaxLength(80).IsRequired();
        b.Property(p => p.ReceiptNumber).HasMaxLength(40);

        b.Property(p => p.ConcurrencyToken)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        b.HasIndex(p => p.PublicReferenceHash).IsUnique();
        b.HasIndex(p => p.ProviderCheckoutSessionId);
        b.HasIndex(p => p.ProviderPaymentId);
        b.HasIndex(p => p.IdempotencyKey);
        b.HasIndex(p => new { p.TenantId, p.Status });
        b.HasIndex(p => new { p.FeeQuoteId, p.Status });
        b.HasIndex(p => new { p.TenantId, p.Provider, p.Status }); // cash reconciliation reports
    }
}

public sealed class WebhookEventConfiguration : IEntityTypeConfiguration<WebhookEvent>
{
    public void Configure(EntityTypeBuilder<WebhookEvent> b)
    {
        b.ToTable("webhook_events");
        b.HasKey(e => e.Id);
        b.Property(e => e.Provider).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.ProviderEventId).HasMaxLength(128).IsRequired();
        b.Property(e => e.EventType).HasMaxLength(80);
        b.Property(e => e.PayloadHash).HasMaxLength(64);
        b.Property(e => e.ProcessingStatus).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.FailureReason).HasMaxLength(1000);

        // Authoritative idempotency guard for webhook processing.
        b.HasIndex(e => new { e.Provider, e.ProviderEventId }).IsUnique();
        b.HasIndex(e => e.PaymentId);
    }
}
