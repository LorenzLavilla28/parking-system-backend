using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkingSaaS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MakePayMongoLiveOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Test-mode connections are intentionally retired. Live rows keep their
            // existing secret ARN, webhook token, status, and account metadata.
            migrationBuilder.Sql(
                "DELETE FROM \"tenant_paymongo_connections\" WHERE \"Environment\" <> 'live';");

            migrationBuilder.DropIndex(
                name: "IX_tenant_paymongo_connections_TenantId_Environment",
                table: "tenant_paymongo_connections");

            // Keep the legacy column for one compatibility window so the previous
            // API image can still run if deployment health checks trigger rollback.
            // The new model does not map it, and the database always supplies live.
            migrationBuilder.Sql(
                "ALTER TABLE \"tenant_paymongo_connections\" ALTER COLUMN \"Environment\" SET DEFAULT 'live';");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_paymongo_connections_TenantId",
                table: "tenant_paymongo_connections",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tenant_paymongo_connections_TenantId",
                table: "tenant_paymongo_connections");

            migrationBuilder.Sql(
                "ALTER TABLE \"tenant_paymongo_connections\" ALTER COLUMN \"Environment\" DROP DEFAULT;");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_paymongo_connections_TenantId_Environment",
                table: "tenant_paymongo_connections",
                columns: new[] { "TenantId", "Environment" },
                unique: true);
        }
    }
}
