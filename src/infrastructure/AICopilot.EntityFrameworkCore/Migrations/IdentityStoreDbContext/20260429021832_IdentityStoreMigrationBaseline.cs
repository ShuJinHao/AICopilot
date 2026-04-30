using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.IdentityStoreDbContext
{
    /// <inheritdoc />
    public partial class IdentityStoreMigrationBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Baseline only. AiCopilotDbContext migration history already created identity.AspNet*.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Snapshot-only baseline. Physical identity tables remain owned by the active database.
        }
    }
}
