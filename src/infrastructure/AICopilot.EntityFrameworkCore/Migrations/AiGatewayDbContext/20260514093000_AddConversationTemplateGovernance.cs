using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.AiGatewayDbContext
{
    /// <inheritdoc />
    [DbContext(typeof(EntityFrameworkCore.AiGatewayDbContext))]
    [Migration("20260514093000_AddConversationTemplateGovernance")]
    public partial class AddConversationTemplateGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "built_in_version",
                schema: "aigateway",
                table: "conversation_templates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "code",
                schema: "aigateway",
                table: "conversation_templates",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_built_in",
                schema: "aigateway",
                table: "conversation_templates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "scope",
                schema: "aigateway",
                table: "conversation_templates",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "General");

            migrationBuilder.CreateIndex(
                name: "IX_conversation_templates_code",
                schema: "aigateway",
                table: "conversation_templates",
                column: "code",
                unique: true,
                filter: "code IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_conversation_templates_code",
                schema: "aigateway",
                table: "conversation_templates");

            migrationBuilder.DropColumn(
                name: "built_in_version",
                schema: "aigateway",
                table: "conversation_templates");

            migrationBuilder.DropColumn(
                name: "code",
                schema: "aigateway",
                table: "conversation_templates");

            migrationBuilder.DropColumn(
                name: "is_built_in",
                schema: "aigateway",
                table: "conversation_templates");

            migrationBuilder.DropColumn(
                name: "scope",
                schema: "aigateway",
                table: "conversation_templates");
        }
    }
}
