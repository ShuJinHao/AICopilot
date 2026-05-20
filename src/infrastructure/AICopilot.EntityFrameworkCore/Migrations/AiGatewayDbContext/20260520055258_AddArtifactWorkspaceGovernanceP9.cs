using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.AiGatewayDbContext
{
    /// <inheritdoc />
    public partial class AddArtifactWorkspaceGovernanceP9 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "boundary",
                schema: "aigateway",
                table: "artifacts",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "finalized_at",
                schema: "aigateway",
                table: "artifacts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_sandbox",
                schema: "aigateway",
                table: "artifacts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_simulation",
                schema: "aigateway",
                table: "artifacts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_truncated",
                schema: "aigateway",
                table: "artifacts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "query_hash",
                schema: "aigateway",
                table: "artifacts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "result_hash",
                schema: "aigateway",
                table: "artifacts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "row_count",
                schema: "aigateway",
                table: "artifacts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "source_label",
                schema: "aigateway",
                table: "artifacts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_mode",
                schema: "aigateway",
                table: "artifacts",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "boundary",
                schema: "aigateway",
                table: "artifacts");

            migrationBuilder.DropColumn(
                name: "finalized_at",
                schema: "aigateway",
                table: "artifacts");

            migrationBuilder.DropColumn(
                name: "is_sandbox",
                schema: "aigateway",
                table: "artifacts");

            migrationBuilder.DropColumn(
                name: "is_simulation",
                schema: "aigateway",
                table: "artifacts");

            migrationBuilder.DropColumn(
                name: "is_truncated",
                schema: "aigateway",
                table: "artifacts");

            migrationBuilder.DropColumn(
                name: "query_hash",
                schema: "aigateway",
                table: "artifacts");

            migrationBuilder.DropColumn(
                name: "result_hash",
                schema: "aigateway",
                table: "artifacts");

            migrationBuilder.DropColumn(
                name: "row_count",
                schema: "aigateway",
                table: "artifacts");

            migrationBuilder.DropColumn(
                name: "source_label",
                schema: "aigateway",
                table: "artifacts");

            migrationBuilder.DropColumn(
                name: "source_mode",
                schema: "aigateway",
                table: "artifacts");
        }
    }
}
