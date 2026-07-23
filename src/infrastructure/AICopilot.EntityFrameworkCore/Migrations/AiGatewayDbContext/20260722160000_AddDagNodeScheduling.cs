using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.AiGatewayDbContext;

[DbContext(typeof(AICopilot.EntityFrameworkCore.AiGatewayDbContext))]
[Migration("20260722160000_AddDagNodeScheduling")]
public partial class AddDagNodeScheduling : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "join_policy",
            schema: "aigateway",
            table: "agent_node_runs",
            type: "character varying(40)",
            maxLength: 40,
            nullable: true);

    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "join_policy",
            schema: "aigateway",
            table: "agent_node_runs");
    }
}
