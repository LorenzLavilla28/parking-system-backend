using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkingSaaS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddParkingSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "parking_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParkingLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlateNumberRaw = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PlateNumberNormalized = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    VehicleType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    VehicleColor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    EntryTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExitTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RatePlanVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PublicTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PublicTokenProtected = table.Column<string>(type: "text", nullable: false),
                    TicketCodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TicketCodeProtected = table.Column<string>(type: "text", nullable: false),
                    CreatedByGuardId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExitedByGuardId = table.Column<Guid>(type: "uuid", nullable: true),
                    EntryPhotoUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ExitPhotoUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PaidExitDeadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinalFee = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    TotalPaid = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parking_sessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_parking_sessions_EntryTime",
                table: "parking_sessions",
                column: "EntryTime");

            migrationBuilder.CreateIndex(
                name: "IX_parking_sessions_ExitTime",
                table: "parking_sessions",
                column: "ExitTime");

            migrationBuilder.CreateIndex(
                name: "IX_parking_sessions_ParkingLocationId_Status_PlateNumberNormal~",
                table: "parking_sessions",
                columns: new[] { "ParkingLocationId", "Status", "PlateNumberNormalized" });

            migrationBuilder.CreateIndex(
                name: "IX_parking_sessions_ParkingLocationId_TicketCodeHash",
                table: "parking_sessions",
                columns: new[] { "ParkingLocationId", "TicketCodeHash" });

            migrationBuilder.CreateIndex(
                name: "IX_parking_sessions_PublicTokenHash",
                table: "parking_sessions",
                column: "PublicTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_parking_sessions_TenantId",
                table: "parking_sessions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_parking_sessions_TenantId_ParkingLocationId",
                table: "parking_sessions",
                columns: new[] { "TenantId", "ParkingLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_parking_sessions_TenantId_PlateNumberNormalized",
                table: "parking_sessions",
                columns: new[] { "TenantId", "PlateNumberNormalized" });

            migrationBuilder.CreateIndex(
                name: "ux_active_session_per_plate_location",
                table: "parking_sessions",
                columns: new[] { "ParkingLocationId", "PlateNumberNormalized" },
                unique: true,
                filter: "\"Status\" IN ('ActiveUnpaid','PaymentPending','PaidExitWindow','OverstayDue')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "parking_sessions");
        }
    }
}
