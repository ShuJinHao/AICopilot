using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.DataAnalysisDbContext
{
    /// <inheritdoc />
    public partial class AddDataSourcePermissionGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "data_source_permission_grants",
                schema: "dataanalysis",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    data_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    target_value = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    can_query = table.Column<bool>(type: "boolean", nullable: false),
                    can_schema_view = table.Column<bool>(type: "boolean", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_source_permission_grants", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_data_source_permission_grants_data_source_id",
                schema: "dataanalysis",
                table: "data_source_permission_grants",
                column: "data_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_source_permission_grants_target",
                schema: "dataanalysis",
                table: "data_source_permission_grants",
                columns: new[] { "data_source_id", "target_type", "target_value" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_source_permission_grants",
                schema: "dataanalysis");
        }
    }
}
