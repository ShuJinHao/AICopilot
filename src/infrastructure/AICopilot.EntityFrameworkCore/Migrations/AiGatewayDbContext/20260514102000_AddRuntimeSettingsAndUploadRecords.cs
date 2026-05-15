using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.AiGatewayDbContext
{
    /// <inheritdoc />
    [DbContext(typeof(EntityFrameworkCore.AiGatewayDbContext))]
    [Migration("20260514102000_AddRuntimeSettingsAndUploadRecords")]
    public partial class AddRuntimeSettingsAndUploadRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chat_runtime_settings",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    routing_history_count = table.Column<int>(type: "integer", nullable: false),
                    answer_history_count = table.Column<int>(type: "integer", nullable: false),
                    rag_rewrite_history_count = table.Column<int>(type: "integer", nullable: false),
                    agent_planning_history_count = table.Column<int>(type: "integer", nullable: false),
                    summary_threshold_messages = table.Column<int>(type: "integer", nullable: false),
                    context_token_limit = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_runtime_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "upload_records",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    scope = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    agent_task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    knowledge_base_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rag_document_id = table.Column<int>(type: "integer", nullable: true),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    storage_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_upload_records", x => x.id);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO aigateway.chat_runtime_settings (
                    id,
                    routing_history_count,
                    answer_history_count,
                    rag_rewrite_history_count,
                    agent_planning_history_count,
                    summary_threshold_messages,
                    context_token_limit,
                    created_at,
                    updated_at
                )
                VALUES (
                    '11111111-1111-4111-8111-111111111111',
                    4,
                    2,
                    4,
                    6,
                    20,
                    24000,
                    TIMESTAMPTZ '2026-05-14 00:00:00+00',
                    TIMESTAMPTZ '2026-05-14 00:00:00+00'
                )
                ON CONFLICT (id) DO NOTHING;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_upload_records_knowledge_base_id",
                schema: "aigateway",
                table: "upload_records",
                column: "knowledge_base_id");

            migrationBuilder.CreateIndex(
                name: "ix_upload_records_user_agent_task",
                schema: "aigateway",
                table: "upload_records",
                columns: new[] { "user_id", "agent_task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_upload_records_user_session",
                schema: "aigateway",
                table: "upload_records",
                columns: new[] { "user_id", "session_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_runtime_settings",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "upload_records",
                schema: "aigateway");
        }
    }
}
