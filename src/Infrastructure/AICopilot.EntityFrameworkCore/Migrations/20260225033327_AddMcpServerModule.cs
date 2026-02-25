using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddMcpServerModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mcp_server_info",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    transport_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    command = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    arguments = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mcp_server_info", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mcp_server_info_name",
                table: "mcp_server_info",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mcp_server_info");
        }
    }
}
