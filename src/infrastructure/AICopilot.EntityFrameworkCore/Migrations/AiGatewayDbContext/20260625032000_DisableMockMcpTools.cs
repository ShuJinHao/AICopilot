using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.AiGatewayDbContext;

[Migration("20260625032000_DisableMockMcpTools")]
[DbContext(typeof(global::AICopilot.EntityFrameworkCore.AiGatewayDbContext))]
public partial class DisableMockMcpTools : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE aigateway.tool_registrations
            SET is_enabled = FALSE,
                is_visible_to_planner = FALSE,
                is_executable_by_agent = FALSE,
                updated_at = NOW()
            WHERE provider_type = 'MockMcp';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
