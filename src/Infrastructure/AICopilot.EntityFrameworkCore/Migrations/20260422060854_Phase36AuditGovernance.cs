using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class Phase36AuditGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_group = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    action_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    target_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    target_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    target_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    operator_user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    operator_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    operator_role_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    result = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    changed_fields = table.Column<string[]>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_action_group_created_at",
                table: "audit_logs",
                columns: new[] { "action_group", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_created_at",
                table: "audit_logs",
                column: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs");
        }
    }
}
