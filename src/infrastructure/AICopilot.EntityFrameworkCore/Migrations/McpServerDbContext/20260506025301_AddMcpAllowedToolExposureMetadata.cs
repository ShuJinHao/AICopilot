using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.McpServerDbContext
{
    /// <inheritdoc />
    public partial class AddMcpAllowedToolExposureMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "allowed_tools",
                schema: "mcp",
                table: "mcp_server_info",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.Sql(
                """
                UPDATE mcp.mcp_server_info
                SET allowed_tools = COALESCE(
                    (
                        SELECT jsonb_agg(jsonb_build_object(
                            'toolName', btrim(tool_name),
                            'externalSystemType', NULL,
                            'capabilityKind', NULL,
                            'riskLevel', NULL,
                            'readOnlyDeclared', external_system_type = 'CloudReadOnly'
                        ))
                        FROM unnest(allowed_tool_names) AS tool_name
                        WHERE NULLIF(btrim(tool_name), '') IS NOT NULL
                    ),
                    '[]'::jsonb
                );
                """);

            migrationBuilder.DropColumn(
                name: "allowed_tool_names",
                schema: "mcp",
                table: "mcp_server_info");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "allowed_tool_names",
                schema: "mcp",
                table: "mcp_server_info",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.Sql(
                """
                UPDATE mcp.mcp_server_info
                SET allowed_tool_names = COALESCE(
                    ARRAY(
                        SELECT tool_item ->> 'toolName'
                        FROM jsonb_array_elements(allowed_tools) AS tool_item
                        WHERE NULLIF(btrim(tool_item ->> 'toolName'), '') IS NOT NULL
                    ),
                    ARRAY[]::text[]
                );
                """);

            migrationBuilder.DropColumn(
                name: "allowed_tools",
                schema: "mcp",
                table: "mcp_server_info");
        }
    }
}
