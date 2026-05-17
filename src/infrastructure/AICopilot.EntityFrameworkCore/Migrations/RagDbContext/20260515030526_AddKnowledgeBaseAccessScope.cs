using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.RagDbContext
{
    /// <inheritdoc />
    public partial class AddKnowledgeBaseAccessScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "access_scope",
                schema: "rag",
                table: "knowledge_bases",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "OwnerOnly");

            migrationBuilder.AddColumn<Guid>(
                name: "owner_user_id",
                schema: "rag",
                table: "knowledge_bases",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_bases_owner_user_id",
                schema: "rag",
                table: "knowledge_bases",
                column: "owner_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_knowledge_bases_owner_user_id",
                schema: "rag",
                table: "knowledge_bases");

            migrationBuilder.DropColumn(
                name: "access_scope",
                schema: "rag",
                table: "knowledge_bases");

            migrationBuilder.DropColumn(
                name: "owner_user_id",
                schema: "rag",
                table: "knowledge_bases");
        }
    }
}
