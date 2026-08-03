using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkingSaaS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCapacityBasedTenantPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CapacityPricingEnabled",
                table: "tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PurchasedSlotCapacityPerLocation",
                table: "tenants",
                type: "integer",
                nullable: true);

            // Existing tenants retain fixed plan pricing until an administrator
            // explicitly changes capacity. Seed their current plan capacity so
            // the platform can display a meaningful baseline in the UI.
            migrationBuilder.Sql("""
                UPDATE tenants
                SET "PurchasedSlotCapacityPerLocation" = CASE "SubscriptionPlan"
                    WHEN 'Starter' THEN 20
                    WHEN 'Growth' THEN 50
                    WHEN 'Enterprise' THEN 90
                    WHEN 'Free' THEN 20
                    ELSE NULL
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CapacityPricingEnabled",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "PurchasedSlotCapacityPerLocation",
                table: "tenants");
        }
    }
}
