using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.AiGatewayDbContext
{
    /// <inheritdoc />
    public partial class UpgradeHarnessRuntimeFromProduction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_evidence_records",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "agent_node_reconciliation_decisions",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "agent_node_runs",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "agent_run_usage_ledger",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "agent_steps",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "agent_task_run_attempts",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "agent_task_run_queue_items",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "agent_worker_heartbeats",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "approval_policies",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "artifact_file_set_operations",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "chat_runtime_settings",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "message_events",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "routing_model_configurations",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "tool_execution_records",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "upload_records",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "agent_tasks",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "approval_requests",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "artifacts",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "artifact_workspaces",
                schema: "aigateway");

            migrationBuilder.DropColumn(
                name: "approval_policy",
                schema: "aigateway",
                table: "tool_registrations");

            migrationBuilder.DropColumn(
                name: "is_visible_to_planner",
                schema: "aigateway",
                table: "tool_registrations");

            migrationBuilder.DropColumn(
                name: "onsite_confirmation_expires_at",
                schema: "aigateway",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "onsite_confirmed_at",
                schema: "aigateway",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "onsite_confirmed_by",
                schema: "aigateway",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "routing_model_id",
                schema: "aigateway",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "routing_model_name",
                schema: "aigateway",
                table: "messages");

            migrationBuilder.CreateTable(
                name: "agent_session_states",
                schema: "aigateway",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    agent_schema_version = table.Column<int>(type: "integer", nullable: false),
                    protected_state = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    active_turn_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    protected_approval_bindings = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_session_states", x => x.session_id);
                    table.ForeignKey(
                        name: "FK_agent_session_states_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "aigateway",
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agent_session_states_user_expiry",
                schema: "aigateway",
                table: "agent_session_states",
                columns: new[] { "user_id", "expires_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "The production Harness upgrade retires persisted tables and cannot be reversed by EF. Restore the verified PostgreSQL backup instead.");
        }
    }
}
