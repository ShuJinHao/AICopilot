using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class Phase42SafetyBoundaryHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "allowed_tool_names",
                table: "mcp_server_info",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string>(
                name: "chat_exposure_mode",
                table: "mcp_server_info",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Disabled");

            migrationBuilder.AlterColumn<string>(
                name: "api_key",
                table: "language_models",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "api_key",
                table: "embedding_models",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "allowed_tool_names",
                table: "mcp_server_info");

            migrationBuilder.DropColumn(
                name: "chat_exposure_mode",
                table: "mcp_server_info");

            migrationBuilder.AlterColumn<string>(
                name: "api_key",
                table: "language_models",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2048)",
                oldMaxLength: 2048,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "api_key",
                table: "embedding_models",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2048)",
                oldMaxLength: 2048,
                oldNullable: true);
        }
    }
}
