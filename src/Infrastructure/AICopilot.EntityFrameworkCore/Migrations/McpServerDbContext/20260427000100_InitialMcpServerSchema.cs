using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.McpServerDbContext
{
    [DbContext(typeof(global::AICopilot.EntityFrameworkCore.McpServerDbContext))]
    [Migration("20260427000100_InitialMcpServerSchema")]
    public partial class InitialMcpServerSchema : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "mcp");

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF to_regclass('public.mcp_server_info') IS NOT NULL
                       AND to_regclass('mcp.mcp_server_info') IS NOT NULL THEN
                        RAISE EXCEPTION
                            'Both public.mcp_server_info and mcp.mcp_server_info exist. Resolve the duplicate MCP schema state before running this migration.';
                    ELSIF to_regclass('public.mcp_server_info') IS NOT NULL THEN
                        ALTER TABLE public.mcp_server_info SET SCHEMA mcp;
                    ELSIF to_regclass('mcp.mcp_server_info') IS NULL THEN
                        CREATE TABLE mcp.mcp_server_info (
                            id uuid NOT NULL,
                            name character varying(100) NOT NULL,
                            description character varying(500) NOT NULL,
                            command character varying(200) NULL,
                            arguments character varying(1000) NOT NULL,
                            chat_exposure_mode character varying(50) NOT NULL,
                            transport_type character varying(50) NOT NULL,
                            is_enabled boolean NOT NULL,
                            allowed_tool_names text[] NOT NULL,
                            CONSTRAINT "PK_mcp_server_info" PRIMARY KEY (id)
                        );

                        CREATE UNIQUE INDEX "IX_mcp_server_info_name"
                            ON mcp.mcp_server_info (name);
                    END IF;
                END $$;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF to_regclass('mcp.mcp_server_info') IS NOT NULL
                       AND to_regclass('public.mcp_server_info') IS NOT NULL THEN
                        RAISE EXCEPTION
                            'Both mcp.mcp_server_info and public.mcp_server_info exist. Resolve the duplicate MCP schema state before rolling back this migration.';
                    ELSIF to_regclass('mcp.mcp_server_info') IS NOT NULL THEN
                        ALTER TABLE mcp.mcp_server_info SET SCHEMA public;
                    END IF;
                END $$;
                """);
        }
    }
}
