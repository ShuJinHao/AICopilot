using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.AiGatewayDbContext
{
    /// <inheritdoc />
    [DbContext(typeof(EntityFrameworkCore.AiGatewayDbContext))]
    [Migration("20260514094000_AddSessionHistoryMetadata")]
    public partial class AddSessionHistoryMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "title",
                schema: "aigateway",
                table: "sessions",
                type: "character varying(48)",
                maxLength: 48,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_message_at",
                schema: "aigateway",
                table: "sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_message_summary",
                schema: "aigateway",
                table: "sessions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "message_count",
                schema: "aigateway",
                table: "sessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_message_at",
                schema: "aigateway",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "last_message_summary",
                schema: "aigateway",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "message_count",
                schema: "aigateway",
                table: "sessions");

            migrationBuilder.AlterColumn<string>(
                name: "title",
                schema: "aigateway",
                table: "sessions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(48)",
                oldMaxLength: 48);
        }
    }
}
