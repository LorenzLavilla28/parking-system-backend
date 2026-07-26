using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using ParkingSaaS.Infrastructure.Persistence;

#nullable disable

namespace ParkingSaaS.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260726000000_AddTenantAdditionalSlotCapacity")]
public partial class AddTenantAdditionalSlotCapacity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "AdditionalSlotCapacity",
            table: "tenants",
            type: "integer",
            nullable: false,
            defaultValue: 0);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AdditionalSlotCapacity",
            table: "tenants");
    }
}
