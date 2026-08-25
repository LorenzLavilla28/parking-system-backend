using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkingSaaS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCorporateBenefitPlateEligibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "corporate_benefit_plates");

            migrationBuilder.DropColumn(
                name: "PlateNumberNormalized",
                table: "corporate_benefit_allocations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PlateNumberNormalized",
                table: "corporate_benefit_allocations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "corporate_benefit_plates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CorporateBenefitProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    PlateNumberDisplay = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PlateNumberNormalized = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_corporate_benefit_plates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_corporate_benefit_plates_corporate_benefit_programs_Corpora~",
                        column: x => x.CorporateBenefitProgramId,
                        principalTable: "corporate_benefit_programs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_corporate_benefit_plates_CorporateBenefitProgramId",
                table: "corporate_benefit_plates",
                column: "CorporateBenefitProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_corporate_benefit_plates_TenantId_CorporateBenefitProgramId~",
                table: "corporate_benefit_plates",
                columns: new[] { "TenantId", "CorporateBenefitProgramId", "PlateNumberNormalized" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_corporate_benefit_plates_TenantId_PlateNumberNormalized_IsA~",
                table: "corporate_benefit_plates",
                columns: new[] { "TenantId", "PlateNumberNormalized", "IsActive" });
        }
    }
}
