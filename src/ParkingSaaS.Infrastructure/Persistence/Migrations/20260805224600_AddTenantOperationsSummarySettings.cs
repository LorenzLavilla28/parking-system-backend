using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkingSaaS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantOperationsSummarySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "OperationsSummaryEnabled",
                table: "tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "OperationsSummaryIntervalHours",
                table: "tenants",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OperationsSummaryLastRunAt",
                table: "tenants",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OperationsSummaryEnabled",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "OperationsSummaryIntervalHours",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "OperationsSummaryLastRunAt",
                table: "tenants");
        }
    }
}
