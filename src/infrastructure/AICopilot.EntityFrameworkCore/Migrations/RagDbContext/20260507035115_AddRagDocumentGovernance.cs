using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.RagDbContext
{
    /// <inheritdoc />
    public partial class AddRagDocumentGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "allowed_for_final_prompt",
                schema: "rag",
                table: "documents",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "blocked_reason",
                schema: "rag",
                table: "documents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "classification",
                schema: "rag",
                table: "documents",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Internal");

            migrationBuilder.AddColumn<DateTime>(
                name: "effective_from",
                schema: "rag",
                table: "documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "effective_to",
                schema: "rag",
                table: "documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_sanitized",
                schema: "rag",
                table: "documents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "reviewed_at",
                schema: "rag",
                table: "documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reviewed_by",
                schema: "rag",
                table: "documents",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_type",
                schema: "rag",
                table: "documents",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "UserUploaded");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "allowed_for_final_prompt",
                schema: "rag",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "blocked_reason",
                schema: "rag",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "classification",
                schema: "rag",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "effective_from",
                schema: "rag",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "effective_to",
                schema: "rag",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "is_sanitized",
                schema: "rag",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "reviewed_at",
                schema: "rag",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "reviewed_by",
                schema: "rag",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "source_type",
                schema: "rag",
                table: "documents");
        }
    }
}
