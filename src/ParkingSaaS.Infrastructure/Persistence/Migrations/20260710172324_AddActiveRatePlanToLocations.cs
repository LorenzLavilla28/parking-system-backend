using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkingSaaS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActiveRatePlanToLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActiveRatePlanId",
                table: "parking_locations",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActiveRatePlanId",
                table: "parking_locations");
        }
    }
}
