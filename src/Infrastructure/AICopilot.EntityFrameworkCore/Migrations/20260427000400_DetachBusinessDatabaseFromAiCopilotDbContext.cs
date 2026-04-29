using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations
{
    [DbContext(typeof(global::AICopilot.EntityFrameworkCore.AiCopilotDbContext))]
    [Migration("20260427000400_DetachBusinessDatabaseFromAiCopilotDbContext")]
    public partial class DetachBusinessDatabaseFromAiCopilotDbContext : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
