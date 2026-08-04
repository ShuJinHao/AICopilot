using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.AiGatewayDbContext
{
    /// <inheritdoc />
    public partial class AddToolGovernanceP4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "approval_policy",
                schema: "aigateway",
                table: "tool_registrations",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string[]>(
                name: "business_domains",
                schema: "aigateway",
                table: "tool_registrations",
                type: "text[]",
                nullable: false,
                defaultValue: new string[] { });

            migrationBuilder.AddColumn<int>(
                name: "catalog_version",
                schema: "aigateway",
                table: "tool_registrations",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "category",
                schema: "aigateway",
                table: "tool_registrations",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "General");

            migrationBuilder.AddColumn<string>(
                name: "data_boundary",
                schema: "aigateway",
                table: "tool_registrations",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "NoData");

            migrationBuilder.AddColumn<bool>(
                name: "is_executable_by_agent",
                schema: "aigateway",
                table: "tool_registrations",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_visible_to_planner",
                schema: "aigateway",
                table: "tool_registrations",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "schema_version",
                schema: "aigateway",
                table: "tool_registrations",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "approval_policy",
                schema: "aigateway",
                table: "tool_registrations");

            migrationBuilder.DropColumn(
                name: "business_domains",
                schema: "aigateway",
                table: "tool_registrations");

            migrationBuilder.DropColumn(
                name: "catalog_version",
                schema: "aigateway",
                table: "tool_registrations");

            migrationBuilder.DropColumn(
                name: "category",
                schema: "aigateway",
                table: "tool_registrations");

            migrationBuilder.DropColumn(
                name: "data_boundary",
                schema: "aigateway",
                table: "tool_registrations");

            migrationBuilder.DropColumn(
                name: "is_executable_by_agent",
                schema: "aigateway",
                table: "tool_registrations");

            migrationBuilder.DropColumn(
                name: "is_visible_to_planner",
                schema: "aigateway",
                table: "tool_registrations");

            migrationBuilder.DropColumn(
                name: "schema_version",
                schema: "aigateway",
                table: "tool_registrations");
        }
    }
}
