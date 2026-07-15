using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParkingSaaS.Domain.Pricing;

namespace ParkingSaaS.Infrastructure.Persistence.Configurations;

public sealed class FeeQuoteConfiguration : IEntityTypeConfiguration<FeeQuote>
{
    public void Configure(EntityTypeBuilder<FeeQuote> b)
    {
        b.ToTable("fee_quotes");
        b.HasKey(q => q.Id);
        b.Property(q => q.TenantId).IsRequired();
        b.Property(q => q.ParkingSessionId).IsRequired();
        b.Property(q => q.Currency).HasMaxLength(3).IsRequired();
        b.Property(q => q.BaseAmount).HasColumnType("numeric(12,2)");
        b.Property(q => q.DiscountAmount).HasColumnType("numeric(12,2)");
        b.Property(q => q.TotalAmount).HasColumnType("numeric(12,2)");
        b.Property(q => q.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(q => q.PricingBreakdownJson).HasColumnType("jsonb");

        b.HasIndex(q => new { q.TenantId, q.ParkingSessionId, q.Status });
        b.HasIndex(q => q.ExpiresAt);
    }
}
