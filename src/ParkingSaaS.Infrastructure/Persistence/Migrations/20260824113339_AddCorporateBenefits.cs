using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkingSaaS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCorporateBenefits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CorporateBenefitAllocationId",
                table: "parking_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "corporate_benefit_programs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_corporate_benefit_programs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "corporate_benefit_plates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorporateBenefitProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlateNumberNormalized = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PlateNumberDisplay = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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

            migrationBuilder.CreateTable(
                name: "corporate_benefit_program_locations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorporateBenefitProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParkingLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MaxConcurrentFreeSessions = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_corporate_benefit_program_locations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_corporate_benefit_program_locations_corporate_benefit_progr~",
                        column: x => x.CorporateBenefitProgramId,
                        principalTable: "corporate_benefit_programs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_corporate_benefit_program_locations_parking_locations_Parki~",
                        column: x => x.ParkingLocationId,
                        principalTable: "parking_locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "corporate_benefit_program_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorporateBenefitProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RulesJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_corporate_benefit_program_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_corporate_benefit_program_versions_corporate_benefit_progra~",
                        column: x => x.CorporateBenefitProgramId,
                        principalTable: "corporate_benefit_programs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "corporate_benefit_allocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorporateBenefitProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorporateBenefitProgramVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParkingLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParkingSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlateNumberNormalized = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AllocatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_corporate_benefit_allocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_corporate_benefit_allocations_corporate_benefit_program_ver~",
                        column: x => x.CorporateBenefitProgramVersionId,
                        principalTable: "corporate_benefit_program_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_corporate_benefit_allocations_corporate_benefit_programs_Co~",
                        column: x => x.CorporateBenefitProgramId,
                        principalTable: "corporate_benefit_programs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_corporate_benefit_allocations_parking_locations_ParkingLoca~",
                        column: x => x.ParkingLocationId,
                        principalTable: "parking_locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_corporate_benefit_allocations_parking_sessions_ParkingSessi~",
                        column: x => x.ParkingSessionId,
                        principalTable: "parking_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_parking_sessions_CorporateBenefitAllocationId",
                table: "parking_sessions",
                column: "CorporateBenefitAllocationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_corporate_benefit_allocations_CorporateBenefitProgramId",
                table: "corporate_benefit_allocations",
                column: "CorporateBenefitProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_corporate_benefit_allocations_CorporateBenefitProgramVersio~",
                table: "corporate_benefit_allocations",
                column: "CorporateBenefitProgramVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_corporate_benefit_allocations_ParkingLocationId_Status",
                table: "corporate_benefit_allocations",
                columns: new[] { "ParkingLocationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_corporate_benefit_allocations_ParkingSessionId",
                table: "corporate_benefit_allocations",
                column: "ParkingSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_corporate_benefit_allocations_TenantId_CorporateBenefitProg~",
                table: "corporate_benefit_allocations",
                columns: new[] { "TenantId", "CorporateBenefitProgramId", "ParkingLocationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_corporate_benefit_allocations_TenantId_ParkingSessionId",
                table: "corporate_benefit_allocations",
                columns: new[] { "TenantId", "ParkingSessionId" },
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_corporate_benefit_program_locations_CorporateBenefitProgram~",
                table: "corporate_benefit_program_locations",
                column: "CorporateBenefitProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_corporate_benefit_program_locations_ParkingLocationId",
                table: "corporate_benefit_program_locations",
                column: "ParkingLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_corporate_benefit_program_locations_TenantId_CorporateBenef~",
                table: "corporate_benefit_program_locations",
                columns: new[] { "TenantId", "CorporateBenefitProgramId", "ParkingLocationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_corporate_benefit_program_locations_TenantId_ParkingLocatio~",
                table: "corporate_benefit_program_locations",
                columns: new[] { "TenantId", "ParkingLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_corporate_benefit_program_versions_CorporateBenefitProgram~1",
                table: "corporate_benefit_program_versions",
                columns: new[] { "CorporateBenefitProgramId", "EffectiveFrom", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "IX_corporate_benefit_program_versions_CorporateBenefitProgramI~",
                table: "corporate_benefit_program_versions",
                columns: new[] { "CorporateBenefitProgramId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_corporate_benefit_programs_TenantId_Priority",
                table: "corporate_benefit_programs",
                columns: new[] { "TenantId", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_corporate_benefit_programs_TenantId_Status",
                table: "corporate_benefit_programs",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "corporate_benefit_allocations");

            migrationBuilder.DropTable(
                name: "corporate_benefit_plates");

            migrationBuilder.DropTable(
                name: "corporate_benefit_program_locations");

            migrationBuilder.DropTable(
                name: "corporate_benefit_program_versions");

            migrationBuilder.DropTable(
                name: "corporate_benefit_programs");

            migrationBuilder.DropIndex(
                name: "IX_parking_sessions_CorporateBenefitAllocationId",
                table: "parking_sessions");

            migrationBuilder.DropColumn(
                name: "CorporateBenefitAllocationId",
                table: "parking_sessions");
        }
    }
}
