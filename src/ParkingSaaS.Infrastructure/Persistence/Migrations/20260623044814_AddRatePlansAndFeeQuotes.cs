using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkingSaaS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRatePlansAndFeeQuotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fee_quotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParkingSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    BaseAmount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PricingBreakdownJson = table.Column<string>(type: "jsonb", nullable: false),
                    RatePlanVersionId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fee_quotes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "rate_plan_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RatePlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RulesJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rate_plan_versions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "rate_plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParkingLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rate_plans", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fee_quotes_ExpiresAt",
                table: "fee_quotes",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_fee_quotes_TenantId_ParkingSessionId_Status",
                table: "fee_quotes",
                columns: new[] { "TenantId", "ParkingSessionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_rate_plan_versions_RatePlanId_EffectiveFrom_EffectiveTo",
                table: "rate_plan_versions",
                columns: new[] { "RatePlanId", "EffectiveFrom", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "IX_rate_plan_versions_RatePlanId_VersionNumber",
                table: "rate_plan_versions",
                columns: new[] { "RatePlanId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rate_plans_ParkingLocationId_Status",
                table: "rate_plans",
                columns: new[] { "ParkingLocationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_rate_plans_TenantId_ParkingLocationId",
                table: "rate_plans",
                columns: new[] { "TenantId", "ParkingLocationId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fee_quotes");

            migrationBuilder.DropTable(
                name: "rate_plan_versions");

            migrationBuilder.DropTable(
                name: "rate_plans");
        }
    }
}
