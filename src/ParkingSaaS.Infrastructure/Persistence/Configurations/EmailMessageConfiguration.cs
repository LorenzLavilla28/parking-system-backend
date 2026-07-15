using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParkingSaaS.Domain.Emails;

namespace ParkingSaaS.Infrastructure.Persistence.Configurations;

public sealed class EmailMessageConfiguration : IEntityTypeConfiguration<EmailMessage>
{
    public void Configure(EntityTypeBuilder<EmailMessage> b)
    {
        b.ToTable("emails");
        b.HasKey(e => e.Id);
        b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(30);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.ToEmail).HasMaxLength(256).IsRequired();
        b.Property(e => e.ToName).HasMaxLength(200);
        b.Property(e => e.Subject).HasMaxLength(300).IsRequired();
        b.Property(e => e.HtmlBody).IsRequired();
        b.Property(e => e.TextBody).IsRequired();
        b.Property(e => e.LastError).HasMaxLength(1000);

        b.Property(e => e.ConcurrencyToken)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        // The dispatcher polls due Pending messages ordered by NextAttemptAt.
        b.HasIndex(e => new { e.Status, e.NextAttemptAt });
        b.HasIndex(e => e.TenantId);
    }
}
