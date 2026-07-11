using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddPersistenceCommitMarkerRetentionIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_commit_markers_created_at_utc",
                schema: "persistence",
                table: "commit_markers",
                column: "created_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_commit_markers_created_at_utc",
                schema: "persistence",
                table: "commit_markers");
        }
    }
}
