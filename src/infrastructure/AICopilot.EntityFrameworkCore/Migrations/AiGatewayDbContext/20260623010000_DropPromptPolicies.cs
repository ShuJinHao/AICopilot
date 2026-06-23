using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.AiGatewayDbContext;

[Migration("20260623010000_DropPromptPolicies")]
public partial class DropPromptPolicies : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "prompt_policy_versions",
            schema: "aigateway");

        migrationBuilder.DropTable(
            name: "prompt_policies",
            schema: "aigateway");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "prompt_policies",
            schema: "aigateway",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                active_version_no = table.Column<int>(type: "integer", nullable: true),
                code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                usage = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_prompt_policies", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "prompt_policy_versions",
            schema: "aigateway",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                prompt_policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                context_injection_rules = table.Column<string>(type: "character varying(12000)", maxLength: 12000, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                output_format = table.Column<string>(type: "character varying(12000)", maxLength: 12000, nullable: false),
                safety_constraints = table.Column<string>(type: "character varying(12000)", maxLength: 12000, nullable: false),
                system_prompt = table.Column<string>(type: "character varying(12000)", maxLength: 12000, nullable: false),
                version_no = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_prompt_policy_versions", x => x.id);
                table.ForeignKey(
                    name: "FK_prompt_policy_versions_prompt_policies_prompt_policy_id",
                    column: x => x.prompt_policy_id,
                    principalSchema: "aigateway",
                    principalTable: "prompt_policies",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_prompt_policies_code",
            schema: "aigateway",
            table: "prompt_policies",
            column: "code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_prompt_policy_versions_prompt_policy_id_version_no",
            schema: "aigateway",
            table: "prompt_policy_versions",
            columns: new[] { "prompt_policy_id", "version_no" },
            unique: true);
    }
}
