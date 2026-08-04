using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.AiGatewayDbContext;

[Migration("20260722090000_AddAgentExecutionRuntimeP1")]
[DbContext(typeof(global::AICopilot.EntityFrameworkCore.AiGatewayDbContext))]
public partial class AddAgentExecutionRuntimeP1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateSequence<long>(
            name: "model_quota_fencing_seq",
            schema: "aigateway");

        migrationBuilder.AddColumn<long>(
            name: "run_fencing_token",
            schema: "aigateway",
            table: "agent_tasks",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "task_fencing_token",
            schema: "aigateway",
            table: "agent_task_run_attempts",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<bool>(name: "is_budget_initialized", schema: "aigateway", table: "agent_task_run_attempts", type: "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<string>(name: "budget_policy_version", schema: "aigateway", table: "agent_task_run_attempts", type: "character varying(120)", maxLength: 120, nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<int>(name: "budget_max_nodes", schema: "aigateway", table: "agent_task_run_attempts", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "budget_max_tool_calls", schema: "aigateway", table: "agent_task_run_attempts", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "budget_max_model_calls", schema: "aigateway", table: "agent_task_run_attempts", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "budget_max_input_tokens", schema: "aigateway", table: "agent_task_run_attempts", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "budget_max_output_tokens", schema: "aigateway", table: "agent_task_run_attempts", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "budget_max_elapsed_seconds", schema: "aigateway", table: "agent_task_run_attempts", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<decimal>(name: "budget_max_cost_amount", schema: "aigateway", table: "agent_task_run_attempts", type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<string>(name: "budget_cost_currency", schema: "aigateway", table: "agent_task_run_attempts", type: "character varying(8)", maxLength: 8, nullable: false, defaultValue: "CNY");
        migrationBuilder.AddColumn<int>(name: "budget_max_retries", schema: "aigateway", table: "agent_task_run_attempts", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "budget_max_artifact_count", schema: "aigateway", table: "agent_task_run_attempts", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<long>(name: "budget_max_artifact_bytes", schema: "aigateway", table: "agent_task_run_attempts", type: "bigint", nullable: false, defaultValue: 0L);
        migrationBuilder.AddColumn<int>(name: "budget_reserved_tool_calls", schema: "aigateway", table: "agent_task_run_attempts", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "budget_reserved_model_calls", schema: "aigateway", table: "agent_task_run_attempts", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "budget_reserved_input_tokens", schema: "aigateway", table: "agent_task_run_attempts", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "budget_reserved_output_tokens", schema: "aigateway", table: "agent_task_run_attempts", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<long>(name: "budget_reserved_elapsed_milliseconds", schema: "aigateway", table: "agent_task_run_attempts", type: "bigint", nullable: false, defaultValue: 0L);
        migrationBuilder.AddColumn<decimal>(name: "budget_reserved_cost_amount", schema: "aigateway", table: "agent_task_run_attempts", type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<int>(name: "budget_reserved_retries", schema: "aigateway", table: "agent_task_run_attempts", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "budget_reserved_artifact_count", schema: "aigateway", table: "agent_task_run_attempts", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<long>(name: "budget_reserved_artifact_bytes", schema: "aigateway", table: "agent_task_run_attempts", type: "bigint", nullable: false, defaultValue: 0L);
        migrationBuilder.AddColumn<int>(name: "budget_consumed_tool_calls", schema: "aigateway", table: "agent_task_run_attempts", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "budget_consumed_model_calls", schema: "aigateway", table: "agent_task_run_attempts", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "budget_consumed_input_tokens", schema: "aigateway", table: "agent_task_run_attempts", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "budget_consumed_output_tokens", schema: "aigateway", table: "agent_task_run_attempts", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<long>(name: "budget_consumed_elapsed_milliseconds", schema: "aigateway", table: "agent_task_run_attempts", type: "bigint", nullable: false, defaultValue: 0L);
        migrationBuilder.AddColumn<decimal>(name: "budget_consumed_cost_amount", schema: "aigateway", table: "agent_task_run_attempts", type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<int>(name: "budget_consumed_retries", schema: "aigateway", table: "agent_task_run_attempts", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "budget_consumed_artifact_count", schema: "aigateway", table: "agent_task_run_attempts", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<long>(name: "budget_consumed_artifact_bytes", schema: "aigateway", table: "agent_task_run_attempts", type: "bigint", nullable: false, defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "task_fencing_token",
            schema: "aigateway",
            table: "agent_task_run_queue_items",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.DropIndex(
            name: "ux_agent_task_run_queue_items_active_task",
            schema: "aigateway",
            table: "agent_task_run_queue_items");

        migrationBuilder.Sql(
            """
            UPDATE aigateway.agent_task_run_queue_items
            SET status = 'Claimed'
            WHERE status = 'Leased';
            """);

        migrationBuilder.CreateTable(
            name: "agent_node_runs",
            schema: "aigateway",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                task_id = table.Column<Guid>(type: "uuid", nullable: false),
                run_attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                queue_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                plan_digest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                execution_snapshot_digest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                node_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                node_kind = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                tool_code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                dependencies_json = table.Column<string>(type: "text", nullable: false),
                input_json = table.Column<string>(type: "text", nullable: false),
                input_digest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                output_schema_ref = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                is_required = table.Column<bool>(type: "boolean", nullable: false),
                requires_approval = table.Column<bool>(type: "boolean", nullable: false),
                side_effect_class = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                attempt_no = table.Column<int>(type: "integer", nullable: false),
                max_attempts = table.Column<int>(type: "integer", nullable: false),
                timeout_seconds = table.Column<int>(type: "integer", nullable: false),
                max_tool_calls = table.Column<int>(type: "integer", nullable: false),
                max_model_calls = table.Column<int>(type: "integer", nullable: false),
                max_input_tokens = table.Column<int>(type: "integer", nullable: false),
                max_output_tokens = table.Column<int>(type: "integer", nullable: false),
                max_cost_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                max_artifact_count = table.Column<int>(type: "integer", nullable: false),
                max_artifact_bytes = table.Column<long>(type: "bigint", nullable: false),
                budget_reservation_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                budget_reservation_node_fencing_token = table.Column<long>(type: "bigint", nullable: false),
                reserved_tool_calls = table.Column<int>(type: "integer", nullable: false),
                reserved_model_calls = table.Column<int>(type: "integer", nullable: false),
                reserved_input_tokens = table.Column<int>(type: "integer", nullable: false),
                reserved_output_tokens = table.Column<int>(type: "integer", nullable: false),
                reserved_elapsed_milliseconds = table.Column<long>(type: "bigint", nullable: false),
                reserved_cost_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                reserved_retry_count = table.Column<int>(type: "integer", nullable: false),
                reserved_artifact_count = table.Column<int>(type: "integer", nullable: false),
                reserved_artifact_bytes = table.Column<long>(type: "bigint", nullable: false),
                task_fencing_token = table.Column<long>(type: "bigint", nullable: false),
                node_fencing_token = table.Column<long>(type: "bigint", nullable: false),
                idempotency_generation = table.Column<int>(type: "integer", nullable: false),
                lease_id = table.Column<Guid>(type: "uuid", nullable: true),
                lease_owner = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                idempotency_key_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                provider_operation_code = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                provider_receipt_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                reconciliation_policy = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                last_confirmed_stage = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                integrity_status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                reconciliation_fencing_token = table.Column<long>(type: "bigint", nullable: false),
                reconciliation_attempt_no = table.Column<int>(type: "integer", nullable: false),
                reconciliation_lease_id = table.Column<Guid>(type: "uuid", nullable: true),
                reconciliation_owner = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                reconciliation_lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                reconciliation_deadline_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                requires_manual_resolution = table.Column<bool>(type: "boolean", nullable: false),
                reconciliation_resolution_code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                reconciliation_decision_digest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                reconciled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                output_digest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                evidence_id = table.Column<Guid>(type: "uuid", nullable: true),
                evidence_set_digest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                failure_code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                safe_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_agent_node_runs", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "agent_evidence_records",
            schema: "aigateway",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                session_id = table.Column<Guid>(type: "uuid", nullable: false),
                task_id = table.Column<Guid>(type: "uuid", nullable: false),
                run_attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                node_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                node_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                evidence_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                truth_class = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                storage_mode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                canonical_envelope_json = table.Column<string>(type: "text", nullable: false),
                envelope_digest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                output_digest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                inline_payload_json = table.Column<string>(type: "text", nullable: true),
                payload_ref = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                media_type = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                byte_length = table.Column<int>(type: "integer", nullable: false),
                payload_sha256 = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                allowed_consumer_scope_json = table.Column<string>(type: "text", nullable: false),
                task_fencing_token = table.Column<long>(type: "bigint", nullable: false),
                node_fencing_token = table.Column<long>(type: "bigint", nullable: false),
                is_revoked = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_agent_evidence_records", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "agent_run_usage_ledger",
            schema: "aigateway",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                task_id = table.Column<Guid>(type: "uuid", nullable: false),
                run_attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                node_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                task_fencing_token = table.Column<long>(type: "bigint", nullable: false),
                node_fencing_token = table.Column<long>(type: "bigint", nullable: false),
                input_tokens = table.Column<int>(type: "integer", nullable: false),
                output_tokens = table.Column<int>(type: "integer", nullable: false),
                model_calls = table.Column<int>(type: "integer", nullable: false),
                tool_calls = table.Column<int>(type: "integer", nullable: false),
                elapsed_milliseconds = table.Column<long>(type: "bigint", nullable: false),
                cost_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                artifact_count = table.Column<int>(type: "integer", nullable: false),
                artifact_bytes = table.Column<long>(type: "bigint", nullable: false),
                cost_currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                correlation_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_agent_run_usage_ledger", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "agent_node_reconciliation_decisions",
            schema: "aigateway",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                task_id = table.Column<Guid>(type: "uuid", nullable: false),
                run_attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                node_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                task_fencing_token = table.Column<long>(type: "bigint", nullable: false),
                node_fencing_token = table.Column<long>(type: "bigint", nullable: false),
                reconciliation_fencing_token = table.Column<long>(type: "bigint", nullable: false),
                resolution = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                reason_code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                actor_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                actor_id_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                evidence_digest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                provider_receipt_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                decision_digest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                decided_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_agent_node_reconciliation_decisions", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "model_quota_reservations",
            schema: "aigateway",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_key_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: true),
                role_key_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                model_id = table.Column<Guid>(type: "uuid", nullable: false),
                endpoint_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                pool_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                window_started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                window_ends_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                estimated_input_tokens = table.Column<int>(type: "integer", nullable: false),
                estimated_output_tokens = table.Column<int>(type: "integer", nullable: false),
                actual_input_tokens = table.Column<int>(type: "integer", nullable: false),
                actual_output_tokens = table.Column<int>(type: "integer", nullable: false),
                concurrency_slots = table.Column<int>(type: "integer", nullable: false),
                fencing_token = table.Column<long>(type: "bigint", nullable: false),
                correlation_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                failure_code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                reserved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                settled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_model_quota_reservations", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "artifact_file_set_operations",
            schema: "aigateway",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                commit_id = table.Column<Guid>(type: "uuid", nullable: false),
                task_id = table.Column<Guid>(type: "uuid", nullable: false),
                workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                node_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                task_fencing_token = table.Column<long>(type: "bigint", nullable: false),
                node_fencing_token = table.Column<long>(type: "bigint", nullable: false),
                operation_kind = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                manifest_json = table.Column<string>(type: "text", nullable: false),
                manifest_digest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                staging_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                published_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                published_manifest_digest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                failure_code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                safe_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_artifact_file_set_operations", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ux_agent_task_run_queue_items_active_task",
            schema: "aigateway",
            table: "agent_task_run_queue_items",
            column: "task_id",
            unique: true,
            filter: "status IN ('Queued', 'Claimed', 'Started')");

        migrationBuilder.CreateIndex(
            name: "ix_agent_node_runs_lease_expires_at",
            schema: "aigateway",
            table: "agent_node_runs",
            column: "lease_expires_at");

        migrationBuilder.CreateIndex(
            name: "ix_agent_node_runs_runnable",
            schema: "aigateway",
            table: "agent_node_runs",
            columns: new[] { "run_attempt_id", "status", "next_attempt_at" });

        migrationBuilder.CreateIndex(
            name: "ix_agent_node_runs_reconciliation",
            schema: "aigateway",
            table: "agent_node_runs",
            columns: new[] { "status", "next_attempt_at", "reconciliation_lease_expires_at" });

        migrationBuilder.CreateIndex(
            name: "ux_agent_node_runs_attempt_node",
            schema: "aigateway",
            table: "agent_node_runs",
            columns: new[] { "run_attempt_id", "node_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_agent_node_runs_evidence_id",
            schema: "aigateway",
            table: "agent_node_runs",
            column: "evidence_id",
            unique: true,
            filter: "evidence_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ix_agent_evidence_consumer_scope",
            schema: "aigateway",
            table: "agent_evidence_records",
            columns: new[] { "user_id", "task_id", "created_at" });

        migrationBuilder.CreateIndex(
            name: "ix_agent_evidence_digest",
            schema: "aigateway",
            table: "agent_evidence_records",
            column: "envelope_digest");

        migrationBuilder.CreateIndex(
            name: "ux_agent_evidence_node_fence",
            schema: "aigateway",
            table: "agent_evidence_records",
            columns: new[] { "node_run_id", "node_fencing_token" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_agent_run_usage_attempt",
            schema: "aigateway",
            table: "agent_run_usage_ledger",
            columns: new[] { "task_id", "run_attempt_id" });

        migrationBuilder.CreateIndex(
            name: "ux_agent_run_usage_node_fence",
            schema: "aigateway",
            table: "agent_run_usage_ledger",
            columns: new[] { "node_run_id", "node_fencing_token" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_agent_node_reconciliation_fence",
            schema: "aigateway",
            table: "agent_node_reconciliation_decisions",
            columns: new[] { "node_run_id", "reconciliation_fencing_token" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_agent_node_reconciliation_task_time",
            schema: "aigateway",
            table: "agent_node_reconciliation_decisions",
            columns: new[] { "task_id", "decided_at_utc" });

        migrationBuilder.CreateIndex(
            name: "ux_model_quota_reservations_correlation",
            schema: "aigateway",
            table: "model_quota_reservations",
            column: "correlation_hash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_model_quota_reservations_endpoint_window",
            schema: "aigateway",
            table: "model_quota_reservations",
            columns: new[] { "endpoint_id", "model_id", "window_started_at_utc", "status" });

        migrationBuilder.CreateIndex(
            name: "ix_model_quota_reservations_authority_window",
            schema: "aigateway",
            table: "model_quota_reservations",
            columns: new[] { "tenant_key_hash", "user_id", "role_key_hash", "window_started_at_utc" });

        migrationBuilder.CreateIndex(
            name: "ix_model_quota_reservations_expiry",
            schema: "aigateway",
            table: "model_quota_reservations",
            columns: new[] { "status", "expires_at_utc" });

        migrationBuilder.CreateIndex(
            name: "ux_artifact_file_set_operations_commit",
            schema: "aigateway",
            table: "artifact_file_set_operations",
            column: "commit_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_artifact_file_set_operations_workspace_status",
            schema: "aigateway",
            table: "artifact_file_set_operations",
            columns: new[] { "workspace_id", "status", "created_at_utc" });

        migrationBuilder.CreateIndex(
            name: "ix_artifact_file_set_operations_node_fence",
            schema: "aigateway",
            table: "artifact_file_set_operations",
            columns: new[] { "node_run_id", "task_fencing_token", "node_fencing_token" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "agent_node_reconciliation_decisions",
            schema: "aigateway");

        migrationBuilder.DropTable(
            name: "artifact_file_set_operations",
            schema: "aigateway");

        migrationBuilder.DropTable(
            name: "model_quota_reservations",
            schema: "aigateway");

        migrationBuilder.DropTable(
            name: "agent_evidence_records",
            schema: "aigateway");

        migrationBuilder.DropTable(
            name: "agent_node_runs",
            schema: "aigateway");

        migrationBuilder.DropTable(
            name: "agent_run_usage_ledger",
            schema: "aigateway");

        migrationBuilder.DropIndex(
            name: "ux_agent_task_run_queue_items_active_task",
            schema: "aigateway",
            table: "agent_task_run_queue_items");

        migrationBuilder.Sql(
            """
            UPDATE aigateway.agent_task_run_queue_items
            SET status = 'Leased'
            WHERE status IN ('Claimed', 'Started');
            """);

        migrationBuilder.CreateIndex(
            name: "ux_agent_task_run_queue_items_active_task",
            schema: "aigateway",
            table: "agent_task_run_queue_items",
            column: "task_id",
            unique: true,
            filter: "status IN ('Queued', 'Leased')");

        migrationBuilder.DropColumn(
            name: "run_fencing_token",
            schema: "aigateway",
            table: "agent_tasks");

        foreach (var column in new[]
                 {
                     "is_budget_initialized", "budget_policy_version", "budget_max_nodes",
                     "budget_max_tool_calls", "budget_max_model_calls", "budget_max_input_tokens",
                     "budget_max_output_tokens", "budget_max_elapsed_seconds", "budget_max_cost_amount",
                     "budget_cost_currency", "budget_max_retries", "budget_max_artifact_count",
                     "budget_max_artifact_bytes", "budget_reserved_tool_calls", "budget_reserved_model_calls",
                     "budget_reserved_input_tokens", "budget_reserved_output_tokens",
                     "budget_reserved_elapsed_milliseconds", "budget_reserved_cost_amount",
                     "budget_reserved_retries", "budget_reserved_artifact_count", "budget_reserved_artifact_bytes",
                     "budget_consumed_tool_calls", "budget_consumed_model_calls", "budget_consumed_input_tokens",
                     "budget_consumed_output_tokens", "budget_consumed_elapsed_milliseconds",
                     "budget_consumed_cost_amount", "budget_consumed_retries", "budget_consumed_artifact_count",
                     "budget_consumed_artifact_bytes"
                 })
        {
            migrationBuilder.DropColumn(
                name: column,
                schema: "aigateway",
                table: "agent_task_run_attempts");
        }

        migrationBuilder.DropColumn(
            name: "task_fencing_token",
            schema: "aigateway",
            table: "agent_task_run_attempts");

        migrationBuilder.DropColumn(
            name: "task_fencing_token",
            schema: "aigateway",
            table: "agent_task_run_queue_items");

        migrationBuilder.DropSequence(
            name: "model_quota_fencing_seq",
            schema: "aigateway");
    }
}
