using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.AiGatewayDbContext;

[Migration("20260625052000_RemoveRuntimeSummaryThreshold")]
[DbContext(typeof(global::AICopilot.EntityFrameworkCore.AiGatewayDbContext))]
public partial class RemoveRuntimeSummaryThreshold : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "summary_threshold_messages",
            schema: "aigateway",
            table: "chat_runtime_settings");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "summary_threshold_messages",
            schema: "aigateway",
            table: "chat_runtime_settings",
            type: "integer",
            nullable: false,
            defaultValue: 20);
    }
}
