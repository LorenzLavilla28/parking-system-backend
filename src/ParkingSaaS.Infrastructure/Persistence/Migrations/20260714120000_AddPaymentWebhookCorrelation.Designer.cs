using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ParkingSaaS.Infrastructure.Persistence;

#nullable disable

namespace ParkingSaaS.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260714120000_AddPaymentWebhookCorrelation")]
partial class AddPaymentWebhookCorrelation
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder.HasAnnotation("ProductVersion", "10.0.0");
#pragma warning restore 612, 618
    }
}
