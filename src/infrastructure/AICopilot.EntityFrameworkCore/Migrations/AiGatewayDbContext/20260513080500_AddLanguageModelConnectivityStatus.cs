using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.AiGatewayDbContext
{
    /// <inheritdoc />
    [DbContext(typeof(EntityFrameworkCore.AiGatewayDbContext))]
    [Migration("20260513080500_AddLanguageModelConnectivityStatus")]
    public partial class AddLanguageModelConnectivityStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "connectivity_checked_at",
                schema: "aigateway",
                table: "language_models",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "connectivity_error",
                schema: "aigateway",
                table: "language_models",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "connectivity_status",
                schema: "aigateway",
                table: "language_models",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "connectivity_checked_at",
                schema: "aigateway",
                table: "language_models");

            migrationBuilder.DropColumn(
                name: "connectivity_error",
                schema: "aigateway",
                table: "language_models");

            migrationBuilder.DropColumn(
                name: "connectivity_status",
                schema: "aigateway",
                table: "language_models");
        }
    }
}
