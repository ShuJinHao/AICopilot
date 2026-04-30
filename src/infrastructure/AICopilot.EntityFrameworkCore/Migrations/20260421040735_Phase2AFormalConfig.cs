using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class Phase2AFormalConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_BusinessDatabases",
                table: "BusinessDatabases");

            migrationBuilder.DropColumn(
                name: "sensitive_tools",
                table: "mcp_server_info");

            migrationBuilder.RenameTable(
                name: "BusinessDatabases",
                newName: "business_databases");

            migrationBuilder.RenameColumn(
                name: "Provider",
                table: "business_databases",
                newName: "provider");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "business_databases",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Description",
                table: "business_databases",
                newName: "description");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "business_databases",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "IsEnabled",
                table: "business_databases",
                newName: "is_enabled");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "business_databases",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "ConnectionString",
                table: "business_databases",
                newName: "connection_string");

            migrationBuilder.AlterColumn<string>(
                name: "provider",
                table: "business_databases",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "business_databases",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "description",
                table: "business_databases",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<bool>(
                name: "is_read_only",
                table: "business_databases",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddPrimaryKey(
                name: "PK_business_databases",
                table: "business_databases",
                column: "id");

            migrationBuilder.CreateTable(
                name: "approval_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    target_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    target_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tool_names = table.Column<string[]>(type: "text[]", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_policies", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_business_databases_name",
                table: "business_databases",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_approval_policies_name",
                table: "approval_policies",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "approval_policies");

            migrationBuilder.DropPrimaryKey(
                name: "PK_business_databases",
                table: "business_databases");

            migrationBuilder.DropIndex(
                name: "IX_business_databases_name",
                table: "business_databases");

            migrationBuilder.DropColumn(
                name: "is_read_only",
                table: "business_databases");

            migrationBuilder.RenameTable(
                name: "business_databases",
                newName: "BusinessDatabases");

            migrationBuilder.RenameColumn(
                name: "provider",
                table: "BusinessDatabases",
                newName: "Provider");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "BusinessDatabases",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "description",
                table: "BusinessDatabases",
                newName: "Description");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "BusinessDatabases",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "is_enabled",
                table: "BusinessDatabases",
                newName: "IsEnabled");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "BusinessDatabases",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "connection_string",
                table: "BusinessDatabases",
                newName: "ConnectionString");

            migrationBuilder.AddColumn<List<string>>(
                name: "sensitive_tools",
                table: "mcp_server_info",
                type: "text[]",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Provider",
                table: "BusinessDatabases",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "BusinessDatabases",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "BusinessDatabases",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000);

            migrationBuilder.AddPrimaryKey(
                name: "PK_BusinessDatabases",
                table: "BusinessDatabases",
                column: "Id");
        }
    }
}
