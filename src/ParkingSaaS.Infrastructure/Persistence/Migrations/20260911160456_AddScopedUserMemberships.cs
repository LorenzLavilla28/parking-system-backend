using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkingSaaS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScopedUserMemberships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_user_roles_UserId_Role",
                table: "user_roles");

            migrationBuilder.DropIndex(
                name: "IX_user_parking_locations_UserId_ParkingLocationId",
                table: "user_parking_locations");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "user_roles",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "user_memberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_memberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_memberships_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Preserve the existing scope of every role before the new scoped
            // uniqueness constraint is created. Existing users always have one
            // legacy scope in users.TenantId, including Guid.Empty for platform
            // administrators.
            migrationBuilder.Sql("""
                UPDATE "user_roles" AS r
                SET "TenantId" = u."TenantId"
                FROM "users" AS u
                WHERE r."UserId" = u."Id";
                """);

            // Every existing account receives a membership for its legacy
            // tenant scope. The deterministic UUID avoids requiring an optional
            // PostgreSQL extension during production migration.
            migrationBuilder.Sql("""
                INSERT INTO "user_memberships" ("Id", "UserId", "TenantId", "Status")
                SELECT md5(u."Id"::text || ':' || u."TenantId"::text)::uuid,
                       u."Id", u."TenantId", 'Active'
                FROM "users" AS u;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_user_roles_UserId_TenantId_Role",
                table: "user_roles",
                columns: new[] { "UserId", "TenantId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_parking_locations_UserId_TenantId_ParkingLocationId",
                table: "user_parking_locations",
                columns: new[] { "UserId", "TenantId", "ParkingLocationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_memberships_UserId_TenantId",
                table: "user_memberships",
                columns: new[] { "UserId", "TenantId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_memberships");

            migrationBuilder.DropIndex(
                name: "IX_user_roles_UserId_TenantId_Role",
                table: "user_roles");

            migrationBuilder.DropIndex(
                name: "IX_user_parking_locations_UserId_TenantId_ParkingLocationId",
                table: "user_parking_locations");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "user_roles");

            migrationBuilder.CreateIndex(
                name: "IX_user_roles_UserId_Role",
                table: "user_roles",
                columns: new[] { "UserId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_parking_locations_UserId_ParkingLocationId",
                table: "user_parking_locations",
                columns: new[] { "UserId", "ParkingLocationId" },
                unique: true);
        }
    }
}
