using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkingSaaS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantPayMongoConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_paymongo_connections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Environment = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    PayMongoAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SecretArn = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    WebhookTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WebhookTokenProtected = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LastValidatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_paymongo_connections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tenant_paymongo_connections_PayMongoAccountId",
                table: "tenant_paymongo_connections",
                column: "PayMongoAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_paymongo_connections_TenantId_Environment",
                table: "tenant_paymongo_connections",
                columns: new[] { "TenantId", "Environment" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenant_paymongo_connections_WebhookTokenHash",
                table: "tenant_paymongo_connections",
                column: "WebhookTokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_paymongo_connections");
        }
    }
}
