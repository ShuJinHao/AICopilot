using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.RagDbContext
{
    /// <inheritdoc />
    public partial class AddKnowledgeGovernanceP0 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "knowledge_categories",
                schema: "rag",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    business_domain = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    visibility = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    department = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_knowledge_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_supplements",
                schema: "rag",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    priority = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    effective_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expired_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    document_id = table.Column<int>(type: "integer", nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_knowledge_supplements", x => x.id);
                });

            migrationBuilder.AddColumn<Guid>(
                name: "category_id",
                schema: "rag",
                table: "documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "document_group_id",
                schema: "rag",
                table: "documents",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<int>(
                name: "version_no",
                schema: "rag",
                table: "documents",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "effective_at",
                schema: "rag",
                table: "documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "expired_at",
                schema: "rag",
                table: "documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "superseded_by_document_id",
                schema: "rag",
                table: "documents",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_categories_name",
                schema: "rag",
                table: "knowledge_categories",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_supplements_category_priority",
                schema: "rag",
                table: "knowledge_supplements",
                columns: new[] { "category_id", "priority" });

            migrationBuilder.CreateIndex(
                name: "ix_documents_group_version",
                schema: "rag",
                table: "documents",
                columns: new[] { "document_group_id", "version_no" });

            migrationBuilder.CreateIndex(
                name: "ix_documents_status",
                schema: "rag",
                table: "documents",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "ix_documents_group_version", schema: "rag", table: "documents");
            migrationBuilder.DropIndex(name: "ix_documents_status", schema: "rag", table: "documents");
            migrationBuilder.DropTable(name: "knowledge_supplements", schema: "rag");
            migrationBuilder.DropTable(name: "knowledge_categories", schema: "rag");
            migrationBuilder.DropColumn(name: "category_id", schema: "rag", table: "documents");
            migrationBuilder.DropColumn(name: "document_group_id", schema: "rag", table: "documents");
            migrationBuilder.DropColumn(name: "version_no", schema: "rag", table: "documents");
            migrationBuilder.DropColumn(name: "effective_at", schema: "rag", table: "documents");
            migrationBuilder.DropColumn(name: "expired_at", schema: "rag", table: "documents");
            migrationBuilder.DropColumn(name: "superseded_by_document_id", schema: "rag", table: "documents");
        }
    }
}
