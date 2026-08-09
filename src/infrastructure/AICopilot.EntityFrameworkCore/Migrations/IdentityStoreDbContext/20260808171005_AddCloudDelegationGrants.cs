using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.IdentityStoreDbContext
{
    /// <inheritdoc />
    public partial class AddCloudDelegationGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cloud_delegation_grants",
                schema: "identity",
                columns: table => new
                {
                    GrantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AiUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CloudUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Issuer = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProtectedToken = table.Column<string>(type: "text", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IssuedStatusVersion = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cloud_delegation_grants", x => x.GrantId);
                    table.ForeignKey(
                        name: "FK_cloud_delegation_grants_AspNetUsers_AiUserId",
                        column: x => x.AiUserId,
                        principalSchema: "identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cloud_delegation_grants_AiUserId_ExpiresAtUtc",
                schema: "identity",
                table: "cloud_delegation_grants",
                columns: new[] { "AiUserId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_cloud_delegation_grants_CloudUserId_ExpiresAtUtc",
                schema: "identity",
                table: "cloud_delegation_grants",
                columns: new[] { "CloudUserId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_cloud_delegation_grants_ExpiresAtUtc",
                schema: "identity",
                table: "cloud_delegation_grants",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_cloud_delegation_grants_RevokedAtUtc",
                schema: "identity",
                table: "cloud_delegation_grants",
                column: "RevokedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cloud_delegation_grants",
                schema: "identity");
        }
    }
}
