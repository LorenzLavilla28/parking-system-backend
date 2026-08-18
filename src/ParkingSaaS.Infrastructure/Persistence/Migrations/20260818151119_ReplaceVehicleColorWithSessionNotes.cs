using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkingSaaS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceVehicleColorWithSessionNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "parking_sessions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.DropColumn(
                name: "VehicleColor",
                table: "parking_sessions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VehicleColor",
                table: "parking_sessions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "parking_sessions");
        }
    }
}
