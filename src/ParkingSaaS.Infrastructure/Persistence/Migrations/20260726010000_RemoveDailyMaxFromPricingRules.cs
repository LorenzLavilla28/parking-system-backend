using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkingSaaS.Infrastructure.Persistence.Migrations;

[Migration("20260726010000_RemoveDailyMaxFromPricingRules")]
public partial class RemoveDailyMaxFromPricingRules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Pricing rules are stored as immutable JSONB snapshots. Remove the
        // retired cap from existing versions so it cannot be reintroduced by
        // an old rule payload or interpreted by a future reader.
        migrationBuilder.Sql(
            """
            UPDATE "rate_plan_versions"
            SET "RulesJson" = "RulesJson" - 'dailyMax' - 'DailyMax'
            WHERE "RulesJson" ? 'dailyMax' OR "RulesJson" ? 'DailyMax';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // This is an intentional data cleanup. The removed cap cannot be
        // restored because its prior value is not part of the current model.
    }
}
