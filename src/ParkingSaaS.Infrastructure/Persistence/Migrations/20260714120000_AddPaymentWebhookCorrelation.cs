using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkingSaaS.Infrastructure.Persistence.Migrations;

public partial class AddPaymentWebhookCorrelation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "PaymentId",
            table: "webhook_events",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_webhook_events_PaymentId",
            table: "webhook_events",
            column: "PaymentId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_webhook_events_PaymentId",
            table: "webhook_events");

        migrationBuilder.DropColumn(
            name: "PaymentId",
            table: "webhook_events");
    }
}
