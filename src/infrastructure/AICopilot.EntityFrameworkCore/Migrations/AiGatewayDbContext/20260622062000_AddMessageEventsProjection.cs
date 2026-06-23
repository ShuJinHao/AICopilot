using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.AiGatewayDbContext
{
    /// <inheritdoc />
    [DbContext(typeof(global::AICopilot.EntityFrameworkCore.AiGatewayDbContext))]
    [Migration("20260622062000_AddMessageEventsProjection")]
    public partial class AddMessageEventsProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "message_events",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    event_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    message_id = table.Column<int>(type: "integer", nullable: true),
                    agent_task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    agent_step_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approval_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    artifact_workspace_id = table.Column<Guid>(type: "uuid", nullable: true),
                    artifact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payload_json = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_message_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_message_events_agent_tasks_agent_task_id",
                        column: x => x.agent_task_id,
                        principalSchema: "aigateway",
                        principalTable: "agent_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_message_events_approval_requests_approval_request_id",
                        column: x => x.approval_request_id,
                        principalSchema: "aigateway",
                        principalTable: "approval_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_message_events_artifact_workspaces_artifact_workspace_id",
                        column: x => x.artifact_workspace_id,
                        principalSchema: "aigateway",
                        principalTable: "artifact_workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_message_events_artifacts_artifact_id",
                        column: x => x.artifact_id,
                        principalSchema: "aigateway",
                        principalTable: "artifacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_message_events_messages_message_id",
                        column: x => x.message_id,
                        principalSchema: "aigateway",
                        principalTable: "messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_message_events_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "aigateway",
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO aigateway.message_events (
                    id,
                    session_id,
                    sequence,
                    event_type,
                    created_at,
                    message_id
                )
                SELECT
                    ('00000000-0000-0000-0000-' || lpad(message.id::text, 12, '0'))::uuid,
                    message.session_id,
                    message.sequence,
                    'Message',
                    message.created_at,
                    message.id
                FROM aigateway.messages AS message
                WHERE message.type IN ('User', 'Assistant')
                ON CONFLICT (id) DO NOTHING;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_message_events_agent_task_id",
                schema: "aigateway",
                table: "message_events",
                column: "agent_task_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_events_approval_request_id",
                schema: "aigateway",
                table: "message_events",
                column: "approval_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_events_artifact_id",
                schema: "aigateway",
                table: "message_events",
                column: "artifact_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_events_artifact_workspace_id",
                schema: "aigateway",
                table: "message_events",
                column: "artifact_workspace_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_events_message_id",
                schema: "aigateway",
                table: "message_events",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_events_session_id_sequence",
                schema: "aigateway",
                table: "message_events",
                columns: new[] { "session_id", "sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "message_events",
                schema: "aigateway");
        }
    }
}
