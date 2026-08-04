using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.AiGatewayDbContext;

[DbContext(typeof(AICopilot.EntityFrameworkCore.AiGatewayDbContext))]
[Migration("20260722150000_DropSkillDefinitions")]
public partial class DropSkillDefinitions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "skill_definitions",
            schema: "aigateway");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "skill_definitions",
            schema: "aigateway",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                skill_code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                display_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                allowed_tool_codes = table.Column<string[]>(type: "text[]", nullable: false),
                risk_level = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                approval_policy = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                allowed_data_source_modes = table.Column<string[]>(type: "text[]", nullable: false),
                allowed_knowledge_scopes = table.Column<string[]>(type: "text[]", nullable: false),
                output_component_types = table.Column<string[]>(type: "text[]", nullable: false),
                is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                is_built_in = table.Column<bool>(type: "boolean", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_skill_definitions", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_skill_definitions_skill_code",
            schema: "aigateway",
            table: "skill_definitions",
            column: "skill_code",
            unique: true);
    }
}
