using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddAggregateRowVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "mcp_server_info",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "language_models",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "knowledge_bases",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "embedding_models",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "conversation_templates",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "business_databases",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "approval_policies",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                table: "mcp_server_info");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "language_models");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "knowledge_bases");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "embedding_models");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "conversation_templates");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "business_databases");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "approval_policies");
        }
    }
}
