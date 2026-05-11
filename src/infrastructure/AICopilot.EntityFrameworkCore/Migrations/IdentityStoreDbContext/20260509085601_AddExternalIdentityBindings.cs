using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.IdentityStoreDbContext
{
    /// <inheritdoc />
    public partial class AddExternalIdentityBindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "external_identity_bindings",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExternalUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EmployeeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    EmployeeNo = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DisplayNameSnapshot = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DepartmentIdSnapshot = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DepartmentNameSnapshot = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    StatusVersion = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    AccountEnabledSnapshot = table.Column<bool>(type: "boolean", nullable: false),
                    EmployeeActiveSnapshot = table.Column<bool>(type: "boolean", nullable: false),
                    LastLoginAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSyncAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_identity_bindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_external_identity_bindings_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_external_identity_bindings_Provider_TenantId_ExternalUserId",
                schema: "identity",
                table: "external_identity_bindings",
                columns: new[] { "Provider", "TenantId", "ExternalUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_external_identity_bindings_UserId_Provider",
                schema: "identity",
                table: "external_identity_bindings",
                columns: new[] { "UserId", "Provider" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "external_identity_bindings",
                schema: "identity");
        }
    }
}
