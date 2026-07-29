using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.AiGatewayDbContext;

[DbContext(typeof(global::AICopilot.EntityFrameworkCore.AiGatewayDbContext))]
[Migration("20260729023656_AddFinalOutputApprovalClosure")]
public partial class AddFinalOutputApprovalClosure : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DO $b03$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM aigateway.approval_requests AS approval
                    WHERE approval.approval_type = 'FinalOutput'
                    GROUP BY approval.task_id
                    HAVING COUNT(*) > 1
                ) THEN
                    RAISE EXCEPTION USING
                        ERRCODE = '23514',
                        MESSAGE = 'B03 migration blocked: duplicate final-output approvals require explicit repair.';
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM aigateway.approval_requests AS approval
                    LEFT JOIN aigateway.agent_tasks AS task ON task.id = approval.task_id
                    WHERE approval.approval_type = 'FinalOutput'
                      AND (
                          approval.status NOT IN ('Approved', 'Rejected', 'Cancelled', 'Expired') OR
                          task.id IS NULL OR
                          task.status NOT IN ('Finalized', 'Completed', 'Rejected', 'Failed', 'Cancelled')
                      )
                ) THEN
                    RAISE EXCEPTION USING
                        ERRCODE = '23514',
                        MESSAGE = 'B03 migration blocked: non-terminal approval, active task, or orphaned proofless final-output data exists.';
                END IF;
            END
            $b03$;
            """);

        migrationBuilder.AddColumn<string>(
            name: "final_output_proof_version",
            schema: "aigateway",
            table: "approval_requests",
            type: "character varying(80)",
            maxLength: 80,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "final_output_workspace_id",
            schema: "aigateway",
            table: "approval_requests",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "final_output_final_step_id",
            schema: "aigateway",
            table: "approval_requests",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "final_output_run_attempt_id",
            schema: "aigateway",
            table: "approval_requests",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "final_output_node_run_id",
            schema: "aigateway",
            table: "approval_requests",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "final_output_task_fencing_token",
            schema: "aigateway",
            table: "approval_requests",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "final_output_node_fencing_token",
            schema: "aigateway",
            table: "approval_requests",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "final_output_evidence_set_digest",
            schema: "aigateway",
            table: "approval_requests",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "final_output_manifest_digest",
            schema: "aigateway",
            table: "approval_requests",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "final_output_artifact_bindings_json",
            schema: "aigateway",
            table: "approval_requests",
            type: "jsonb",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "final_output_artifact_binding_digest",
            schema: "aigateway",
            table: "approval_requests",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "final_output_proof_digest",
            schema: "aigateway",
            table: "approval_requests",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "final_output_decision_proof_digest",
            schema: "aigateway",
            table: "approval_requests",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "source_approval_request_id",
            schema: "aigateway",
            table: "agent_task_run_queue_items",
            type: "uuid",
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE aigateway.approval_requests
            SET final_output_proof_version = 'legacy-read-only-v0'
            WHERE approval_type = 'FinalOutput';
            """);

        migrationBuilder.AddCheckConstraint(
            name: "ck_approval_requests_final_output_proof_shape",
            schema: "aigateway",
            table: "approval_requests",
            sql:
            """
            (
                approval_type <> 'FinalOutput' AND
                final_output_proof_version IS NULL AND
                final_output_workspace_id IS NULL AND
                final_output_final_step_id IS NULL AND
                final_output_run_attempt_id IS NULL AND
                final_output_node_run_id IS NULL AND
                final_output_task_fencing_token IS NULL AND
                final_output_node_fencing_token IS NULL AND
                final_output_evidence_set_digest IS NULL AND
                final_output_manifest_digest IS NULL AND
                final_output_artifact_bindings_json IS NULL AND
                final_output_artifact_binding_digest IS NULL AND
                final_output_proof_digest IS NULL AND
                final_output_decision_proof_digest IS NULL
            )
            OR
            (
                approval_type = 'FinalOutput' AND
                (
                    (
                        final_output_proof_version = 'legacy-read-only-v0' AND
                        status IN ('Approved', 'Rejected', 'Cancelled', 'Expired') AND
                        final_output_workspace_id IS NULL AND
                        final_output_final_step_id IS NULL AND
                        final_output_run_attempt_id IS NULL AND
                        final_output_node_run_id IS NULL AND
                        final_output_task_fencing_token IS NULL AND
                        final_output_node_fencing_token IS NULL AND
                        final_output_evidence_set_digest IS NULL AND
                        final_output_manifest_digest IS NULL AND
                        final_output_artifact_bindings_json IS NULL AND
                        final_output_artifact_binding_digest IS NULL AND
                        final_output_proof_digest IS NULL AND
                        final_output_decision_proof_digest IS NULL
                    )
                    OR
                    (
                        final_output_proof_version = 'final-output-approval-v1' AND
                        final_output_workspace_id IS NOT NULL AND
                        final_output_final_step_id IS NOT NULL AND
                        final_output_run_attempt_id IS NOT NULL AND
                        final_output_node_run_id IS NOT NULL AND
                        final_output_task_fencing_token > 0 AND
                        final_output_node_fencing_token > 0 AND
                        final_output_evidence_set_digest ~ '^[0-9a-f]{64}$' AND
                        final_output_manifest_digest ~ '^[0-9a-f]{64}$' AND
                        jsonb_typeof(final_output_artifact_bindings_json) = 'array' AND
                        jsonb_array_length(final_output_artifact_bindings_json) > 0 AND
                        final_output_artifact_binding_digest ~ '^[0-9a-f]{64}$' AND
                        final_output_proof_digest ~ '^[0-9a-f]{64}$' AND
                        (
                            (
                                status = 'Pending' AND
                                approved_by IS NULL AND
                                approved_at IS NULL AND
                                approval_comment IS NULL AND
                                final_output_decision_proof_digest IS NULL
                            )
                            OR
                            (
                                status IN ('Approved', 'Rejected') AND
                                approved_by IS NOT NULL AND
                                approved_at >= created_at AND
                                final_output_decision_proof_digest ~ '^[0-9a-f]{64}$'
                            )
                            OR
                            (
                                status IN ('Cancelled', 'Expired') AND
                                approved_by IS NULL AND
                                approval_comment IS NULL AND
                                approved_at >= created_at AND
                                final_output_decision_proof_digest IS NULL
                            )
                        )
                    )
                )
            )
            """);

        migrationBuilder.AddCheckConstraint(
            name: "ck_agent_task_run_queue_items_source_approval",
            schema: "aigateway",
            table: "agent_task_run_queue_items",
            sql: "source_approval_request_id IS NULL OR trigger_type = 'ApprovalResume'");

        migrationBuilder.CreateIndex(
            name: "ux_approval_requests_final_output_task",
            schema: "aigateway",
            table: "approval_requests",
            columns: new[] { "task_id", "approval_type" },
            unique: true,
            filter: "approval_type = 'FinalOutput'");

        migrationBuilder.CreateIndex(
            name: "ux_agent_task_run_queue_items_source_approval",
            schema: "aigateway",
            table: "agent_task_run_queue_items",
            column: "source_approval_request_id",
            unique: true,
            filter: "source_approval_request_id IS NOT NULL");

        migrationBuilder.AddForeignKey(
            name: "fk_agent_task_run_queue_items_source_approval",
            schema: "aigateway",
            table: "agent_task_run_queue_items",
            column: "source_approval_request_id",
            principalSchema: "aigateway",
            principalTable: "approval_requests",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "fk_agent_task_run_queue_items_source_approval",
            schema: "aigateway",
            table: "agent_task_run_queue_items");

        migrationBuilder.DropCheckConstraint(
            name: "ck_agent_task_run_queue_items_source_approval",
            schema: "aigateway",
            table: "agent_task_run_queue_items");

        migrationBuilder.DropCheckConstraint(
            name: "ck_approval_requests_final_output_proof_shape",
            schema: "aigateway",
            table: "approval_requests");

        migrationBuilder.DropIndex(
            name: "ux_agent_task_run_queue_items_source_approval",
            schema: "aigateway",
            table: "agent_task_run_queue_items");

        migrationBuilder.DropIndex(
            name: "ux_approval_requests_final_output_task",
            schema: "aigateway",
            table: "approval_requests");

        migrationBuilder.DropColumn(
            name: "source_approval_request_id",
            schema: "aigateway",
            table: "agent_task_run_queue_items");

        migrationBuilder.DropColumn(
            name: "final_output_proof_version",
            schema: "aigateway",
            table: "approval_requests");

        migrationBuilder.DropColumn(
            name: "final_output_workspace_id",
            schema: "aigateway",
            table: "approval_requests");

        migrationBuilder.DropColumn(
            name: "final_output_final_step_id",
            schema: "aigateway",
            table: "approval_requests");

        migrationBuilder.DropColumn(
            name: "final_output_run_attempt_id",
            schema: "aigateway",
            table: "approval_requests");

        migrationBuilder.DropColumn(
            name: "final_output_node_run_id",
            schema: "aigateway",
            table: "approval_requests");

        migrationBuilder.DropColumn(
            name: "final_output_task_fencing_token",
            schema: "aigateway",
            table: "approval_requests");

        migrationBuilder.DropColumn(
            name: "final_output_node_fencing_token",
            schema: "aigateway",
            table: "approval_requests");

        migrationBuilder.DropColumn(
            name: "final_output_evidence_set_digest",
            schema: "aigateway",
            table: "approval_requests");

        migrationBuilder.DropColumn(
            name: "final_output_manifest_digest",
            schema: "aigateway",
            table: "approval_requests");

        migrationBuilder.DropColumn(
            name: "final_output_artifact_bindings_json",
            schema: "aigateway",
            table: "approval_requests");

        migrationBuilder.DropColumn(
            name: "final_output_artifact_binding_digest",
            schema: "aigateway",
            table: "approval_requests");

        migrationBuilder.DropColumn(
            name: "final_output_proof_digest",
            schema: "aigateway",
            table: "approval_requests");

        migrationBuilder.DropColumn(
            name: "final_output_decision_proof_digest",
            schema: "aigateway",
            table: "approval_requests");
    }
}
