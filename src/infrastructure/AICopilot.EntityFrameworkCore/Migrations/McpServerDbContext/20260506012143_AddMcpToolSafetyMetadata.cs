using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.McpServerDbContext
{
    /// <inheritdoc />
    public partial class AddMcpToolSafetyMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "capability_kind",
                schema: "mcp",
                table: "mcp_server_info",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Diagnostics");

            migrationBuilder.AddColumn<string>(
                name: "external_system_type",
                schema: "mcp",
                table: "mcp_server_info",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<string>(
                name: "risk_level",
                schema: "mcp",
                table: "mcp_server_info",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "RequiresApproval");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "capability_kind",
                schema: "mcp",
                table: "mcp_server_info");

            migrationBuilder.DropColumn(
                name: "external_system_type",
                schema: "mcp",
                table: "mcp_server_info");

            migrationBuilder.DropColumn(
                name: "risk_level",
                schema: "mcp",
                table: "mcp_server_info");
        }
    }
}
