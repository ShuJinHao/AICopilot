using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.AiGatewayDbContext
{
    /// <inheritdoc />
    public partial class AddDynamicModelRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "context_window_tokens",
                schema: "aigateway",
                table: "messages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "final_model_id",
                schema: "aigateway",
                table: "messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "final_model_name",
                schema: "aigateway",
                table: "messages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_output_tokens",
                schema: "aigateway",
                table: "messages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "routing_model_id",
                schema: "aigateway",
                table: "messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "routing_model_name",
                schema: "aigateway",
                table: "messages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_enabled",
                schema: "aigateway",
                table: "language_models",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "max_output_tokens",
                schema: "aigateway",
                table: "language_models",
                type: "integer",
                nullable: false,
                defaultValue: 1024);

            migrationBuilder.AddColumn<string>(
                name: "protocol_type",
                schema: "aigateway",
                table: "language_models",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "OpenAICompatible");

            migrationBuilder.AddColumn<int>(
                name: "usage",
                schema: "aigateway",
                table: "language_models",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.CreateTable(
                name: "routing_model_configurations",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_routing_model_configurations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_routing_model_configurations_is_active",
                schema: "aigateway",
                table: "routing_model_configurations",
                column: "is_active",
                unique: true,
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "IX_routing_model_configurations_model_id",
                schema: "aigateway",
                table: "routing_model_configurations",
                column: "model_id");

            migrationBuilder.CreateIndex(
                name: "IX_routing_model_configurations_name",
                schema: "aigateway",
                table: "routing_model_configurations",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "routing_model_configurations",
                schema: "aigateway");

            migrationBuilder.DropColumn(
                name: "context_window_tokens",
                schema: "aigateway",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "final_model_id",
                schema: "aigateway",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "final_model_name",
                schema: "aigateway",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "max_output_tokens",
                schema: "aigateway",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "routing_model_id",
                schema: "aigateway",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "routing_model_name",
                schema: "aigateway",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "is_enabled",
                schema: "aigateway",
                table: "language_models");

            migrationBuilder.DropColumn(
                name: "max_output_tokens",
                schema: "aigateway",
                table: "language_models");

            migrationBuilder.DropColumn(
                name: "protocol_type",
                schema: "aigateway",
                table: "language_models");

            migrationBuilder.DropColumn(
                name: "usage",
                schema: "aigateway",
                table: "language_models");
        }
    }
}
