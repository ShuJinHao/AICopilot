using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.DataAnalysisDbContext
{
    /// <inheritdoc />
    public partial class AddDataAnalysisReadOnlyMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "external_system_type",
                schema: "dataanalysis",
                table: "business_databases",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<bool>(
                name: "read_only_credential_verified",
                schema: "dataanalysis",
                table: "business_databases",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "external_system_type",
                schema: "dataanalysis",
                table: "business_databases");

            migrationBuilder.DropColumn(
                name: "read_only_credential_verified",
                schema: "dataanalysis",
                table: "business_databases");
        }
    }
}
