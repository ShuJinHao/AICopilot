using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.DataAnalysisDbContext
{
    /// <inheritdoc />
    public partial class AddEnterpriseDataSourceGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "category",
                schema: "dataanalysis",
                table: "business_databases",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "General");

            migrationBuilder.AddColumn<string>(
                name: "tags",
                schema: "dataanalysis",
                table: "business_databases",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "owner_department",
                schema: "dataanalysis",
                table: "business_databases",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "business_domain",
                schema: "dataanalysis",
                table: "business_databases",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "sensitivity_level",
                schema: "dataanalysis",
                table: "business_databases",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Internal");

            migrationBuilder.AddColumn<int>(
                name: "default_query_limit",
                schema: "dataanalysis",
                table: "business_databases",
                type: "integer",
                nullable: false,
                defaultValue: 200);

            migrationBuilder.AddColumn<int>(
                name: "max_query_limit",
                schema: "dataanalysis",
                table: "business_databases",
                type: "integer",
                nullable: false,
                defaultValue: 1000);

            migrationBuilder.AddColumn<bool>(
                name: "is_selectable_in_chat",
                schema: "dataanalysis",
                table: "business_databases",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_selectable_in_agent",
                schema: "dataanalysis",
                table: "business_databases",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "category", schema: "dataanalysis", table: "business_databases");
            migrationBuilder.DropColumn(name: "tags", schema: "dataanalysis", table: "business_databases");
            migrationBuilder.DropColumn(name: "owner_department", schema: "dataanalysis", table: "business_databases");
            migrationBuilder.DropColumn(name: "business_domain", schema: "dataanalysis", table: "business_databases");
            migrationBuilder.DropColumn(name: "sensitivity_level", schema: "dataanalysis", table: "business_databases");
            migrationBuilder.DropColumn(name: "default_query_limit", schema: "dataanalysis", table: "business_databases");
            migrationBuilder.DropColumn(name: "max_query_limit", schema: "dataanalysis", table: "business_databases");
            migrationBuilder.DropColumn(name: "is_selectable_in_chat", schema: "dataanalysis", table: "business_databases");
            migrationBuilder.DropColumn(name: "is_selectable_in_agent", schema: "dataanalysis", table: "business_databases");
        }
    }
}
