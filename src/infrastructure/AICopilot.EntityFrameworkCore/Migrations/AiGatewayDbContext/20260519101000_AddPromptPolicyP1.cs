using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.AiGatewayDbContext
{
    /// <inheritdoc />
    public partial class AddPromptPolicyP1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "prompt_policies",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    usage = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    active_version_no = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prompt_policies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "prompt_policy_versions",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    prompt_policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    system_prompt = table.Column<string>(type: "character varying(12000)", maxLength: 12000, nullable: false),
                    safety_constraints = table.Column<string>(type: "character varying(12000)", maxLength: 12000, nullable: false),
                    context_injection_rules = table.Column<string>(type: "character varying(12000)", maxLength: 12000, nullable: false),
                    output_format = table.Column<string>(type: "character varying(12000)", maxLength: 12000, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prompt_policy_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_prompt_policy_versions_prompt_policies_prompt_policy_id",
                        column: x => x.prompt_policy_id,
                        principalSchema: "aigateway",
                        principalTable: "prompt_policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_prompt_policies_code",
                schema: "aigateway",
                table: "prompt_policies",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_prompt_policy_versions_prompt_policy_id_version_no",
                schema: "aigateway",
                table: "prompt_policy_versions",
                columns: ["prompt_policy_id", "version_no"],
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "prompt_policy_versions",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "prompt_policies",
                schema: "aigateway");
        }
    }
}
