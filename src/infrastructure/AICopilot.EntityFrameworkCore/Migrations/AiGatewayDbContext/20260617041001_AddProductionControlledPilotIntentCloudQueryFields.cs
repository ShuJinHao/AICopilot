using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.AiGatewayDbContext
{
    /// <inheritdoc />
    public partial class AddProductionControlledPilotIntentCloudQueryFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "device_id",
                schema: "aigateway",
                table: "production_controlled_pilot_intents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pass_station_type_key",
                schema: "aigateway",
                table: "production_controlled_pilot_intents",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "device_id",
                schema: "aigateway",
                table: "production_controlled_pilot_intents");

            migrationBuilder.DropColumn(
                name: "pass_station_type_key",
                schema: "aigateway",
                table: "production_controlled_pilot_intents");
        }
    }
}
