using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.AiGatewayDbContext
{
    /// <inheritdoc />
    public partial class DropLegacyTrialPilotModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pilot_authorization_submissions",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "production_controlled_pilot_intents",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "production_controlled_pilot_runs",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "production_pilot_emergency_stop_states",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "production_pilot_ga_readiness_assessments",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "production_pilot_incidents",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "production_pilot_run_ledgers",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "production_pilot_runs",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "production_pilot_windows",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "trial_risk_issues",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "trial_scenario_runs",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "trial_campaigns",
                schema: "aigateway");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pilot_authorization_submissions",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_purpose = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    data_owner = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    endpoint_codes = table.Column<string[]>(type: "text[]", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    final_owner = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    machine_rejected_reasons = table.Column<string[]>(type: "text[]", nullable: false),
                    machine_validation_status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    max_rows = table.Column<int>(type: "integer", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_user_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    time_range_days = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tool_owner = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    credential_window_planning_approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    credential_window_planning_summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    evidence_artifact_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    evidence_summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    business_scope = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    credential_owner = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    department = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    execution_window_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    execution_window_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    pilot_owner = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    post_run_audit_archive_format = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    rollback_window_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rollback_window_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    secret_reference_name_hash = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    secret_storage_mode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    signed_approval_ref = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    expired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_decision_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_decision_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    last_decision_status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    last_reviewer_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_reviewer_user_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    review_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    emergency_owner = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    rollback_owner = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    rollback_summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pilot_authorization_submissions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "production_controlled_pilot_intents",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    analysis_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    artifact_types = table.Column<string[]>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    endpoint_codes = table.Column<string[]>(type: "text[]", nullable: false),
                    goal_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    intent_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    max_rows = table.Column<int>(type: "integer", nullable: false),
                    pass_station_type_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    rejected_reasons = table.Column<string[]>(type: "text[]", nullable: false),
                    requires_final_approval = table.Column<bool>(type: "boolean", nullable: false),
                    requires_tool_approval = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    time_range_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    time_range_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    warnings = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_controlled_pilot_intents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "production_controlled_pilot_runs",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    analysis_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    approval_status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    artifact_types = table.Column<string[]>(type: "text[]", nullable: false),
                    boundary = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duration_ms = table.Column<long>(type: "bigint", nullable: false),
                    endpoint_code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    executed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    intent_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_production_data = table.Column<bool>(type: "boolean", nullable: false),
                    is_sandbox = table.Column<bool>(type: "boolean", nullable: false),
                    is_simulation = table.Column<bool>(type: "boolean", nullable: false),
                    is_truncated = table.Column<bool>(type: "boolean", nullable: false),
                    pilot_window_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    query_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    result_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    row_count = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    run_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source_mode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    source_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_controlled_pilot_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "production_pilot_emergency_stop_states",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    activated_by = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    cleared_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cleared_by = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    reason = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_pilot_emergency_stop_states", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "production_pilot_ga_readiness_assessments",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    blockers = table.Column<string[]>(type: "text[]", nullable: false),
                    checks_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    endpoint_distribution_json = table.Column<string>(type: "jsonb", nullable: false),
                    failed_runs = table.Column<int>(type: "integer", nullable: false),
                    final_artifact_count = table.Column<int>(type: "integer", nullable: false),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    open_incident_count = table.Column<int>(type: "integer", nullable: false),
                    rejected_runs = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    succeeded_runs = table.Column<int>(type: "integer", nullable: false),
                    timeout_runs = table.Column<int>(type: "integer", nullable: false),
                    total_rows = table.Column<int>(type: "integer", nullable: false),
                    total_runs = table.Column<int>(type: "integer", nullable: false),
                    truncated_runs = table.Column<int>(type: "integer", nullable: false),
                    warnings = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_pilot_ga_readiness_assessments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "production_pilot_incidents",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    owner = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    resolution_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    severity = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    source_ref = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_pilot_incidents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "production_pilot_run_ledgers",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    approval_status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    artifact_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    boundary = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duration_ms = table.Column<long>(type: "bigint", nullable: false),
                    endpoint_code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    executed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    intent_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_truncated = table.Column<bool>(type: "boolean", nullable: false),
                    pilot_window_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    query_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    result_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    row_count = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    run_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source_mode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trial_mode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_pilot_run_ledgers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "production_pilot_runs",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    approval_status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    artifact_types = table.Column<string[]>(type: "text[]", nullable: false),
                    boundary = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duration_ms = table.Column<long>(type: "bigint", nullable: false),
                    endpoint_code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    executed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_production_data = table.Column<bool>(type: "boolean", nullable: false),
                    is_sandbox = table.Column<bool>(type: "boolean", nullable: false),
                    is_simulation = table.Column<bool>(type: "boolean", nullable: false),
                    is_truncated = table.Column<bool>(type: "boolean", nullable: false),
                    pilot_window_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    query_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    result_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    row_count = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    run_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    scenario_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    scenario_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source_mode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    source_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_pilot_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "production_pilot_windows",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    allowed_endpoint_codes = table.Column<string[]>(type: "text[]", nullable: false),
                    approval_policy = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    max_rows = table.Column<int>(type: "integer", nullable: false),
                    max_time_range_days = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    owner_department = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    rollback_policy = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    timeout_ms = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    window_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_pilot_windows", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "trial_campaigns",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    allowed_source_modes = table.Column<string[]>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    owner_department = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    pilot_readiness_status = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trial_campaigns", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "trial_risk_issues",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    owner = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    resolution_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    severity = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    source_ref = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trial_risk_issues", x => x.id);
                    table.ForeignKey(
                        name: "FK_trial_risk_issues_trial_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalSchema: "aigateway",
                        principalTable: "trial_campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trial_scenario_runs",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    approval_status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    artifact_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    boundary = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    query_hashes = table.Column<string[]>(type: "text[]", nullable: false),
                    result_hashes = table.Column<string[]>(type: "text[]", nullable: false),
                    scenario_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    source_mode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trial_mode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trial_scenario_runs", x => x.id);
                    table.ForeignKey(
                        name: "FK_trial_scenario_runs_trial_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalSchema: "aigateway",
                        principalTable: "trial_campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pilot_authorization_submissions_expires_at",
                schema: "aigateway",
                table: "pilot_authorization_submissions",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_pilot_authorization_submissions_requested_by_user_id",
                schema: "aigateway",
                table: "pilot_authorization_submissions",
                column: "requested_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_pilot_authorization_submissions_status",
                schema: "aigateway",
                table: "pilot_authorization_submissions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_pilot_authorization_submissions_updated_at",
                schema: "aigateway",
                table: "pilot_authorization_submissions",
                column: "updated_at");

            migrationBuilder.CreateIndex(
                name: "IX_production_controlled_pilot_intents_created_at",
                schema: "aigateway",
                table: "production_controlled_pilot_intents",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_production_controlled_pilot_intents_goal_hash",
                schema: "aigateway",
                table: "production_controlled_pilot_intents",
                column: "goal_hash");

            migrationBuilder.CreateIndex(
                name: "IX_production_controlled_pilot_intents_intent_id",
                schema: "aigateway",
                table: "production_controlled_pilot_intents",
                column: "intent_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_production_controlled_pilot_runs_boundary",
                schema: "aigateway",
                table: "production_controlled_pilot_runs",
                column: "boundary");

            migrationBuilder.CreateIndex(
                name: "IX_production_controlled_pilot_runs_endpoint_code",
                schema: "aigateway",
                table: "production_controlled_pilot_runs",
                column: "endpoint_code");

            migrationBuilder.CreateIndex(
                name: "IX_production_controlled_pilot_runs_executed_at",
                schema: "aigateway",
                table: "production_controlled_pilot_runs",
                column: "executed_at");

            migrationBuilder.CreateIndex(
                name: "IX_production_controlled_pilot_runs_intent_id",
                schema: "aigateway",
                table: "production_controlled_pilot_runs",
                column: "intent_id");

            migrationBuilder.CreateIndex(
                name: "IX_production_controlled_pilot_runs_run_id",
                schema: "aigateway",
                table: "production_controlled_pilot_runs",
                column: "run_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_production_controlled_pilot_runs_source_mode",
                schema: "aigateway",
                table: "production_controlled_pilot_runs",
                column: "source_mode");

            migrationBuilder.CreateIndex(
                name: "IX_production_controlled_pilot_runs_status",
                schema: "aigateway",
                table: "production_controlled_pilot_runs",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_production_pilot_ga_readiness_assessments_generated_at",
                schema: "aigateway",
                table: "production_pilot_ga_readiness_assessments",
                column: "generated_at");

            migrationBuilder.CreateIndex(
                name: "IX_production_pilot_ga_readiness_assessments_status",
                schema: "aigateway",
                table: "production_pilot_ga_readiness_assessments",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_production_pilot_incidents_severity",
                schema: "aigateway",
                table: "production_pilot_incidents",
                column: "severity");

            migrationBuilder.CreateIndex(
                name: "IX_production_pilot_incidents_status",
                schema: "aigateway",
                table: "production_pilot_incidents",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_production_pilot_incidents_updated_at",
                schema: "aigateway",
                table: "production_pilot_incidents",
                column: "updated_at");

            migrationBuilder.CreateIndex(
                name: "IX_production_pilot_run_ledgers_boundary",
                schema: "aigateway",
                table: "production_pilot_run_ledgers",
                column: "boundary");

            migrationBuilder.CreateIndex(
                name: "IX_production_pilot_run_ledgers_endpoint_code",
                schema: "aigateway",
                table: "production_pilot_run_ledgers",
                column: "endpoint_code");

            migrationBuilder.CreateIndex(
                name: "IX_production_pilot_run_ledgers_executed_at",
                schema: "aigateway",
                table: "production_pilot_run_ledgers",
                column: "executed_at");

            migrationBuilder.CreateIndex(
                name: "IX_production_pilot_run_ledgers_run_id",
                schema: "aigateway",
                table: "production_pilot_run_ledgers",
                column: "run_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_production_pilot_run_ledgers_source_mode",
                schema: "aigateway",
                table: "production_pilot_run_ledgers",
                column: "source_mode");

            migrationBuilder.CreateIndex(
                name: "IX_production_pilot_run_ledgers_status",
                schema: "aigateway",
                table: "production_pilot_run_ledgers",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_production_pilot_run_ledgers_trial_mode",
                schema: "aigateway",
                table: "production_pilot_run_ledgers",
                column: "trial_mode");

            migrationBuilder.CreateIndex(
                name: "IX_production_pilot_runs_boundary",
                schema: "aigateway",
                table: "production_pilot_runs",
                column: "boundary");

            migrationBuilder.CreateIndex(
                name: "IX_production_pilot_runs_endpoint_code",
                schema: "aigateway",
                table: "production_pilot_runs",
                column: "endpoint_code");

            migrationBuilder.CreateIndex(
                name: "IX_production_pilot_runs_executed_at",
                schema: "aigateway",
                table: "production_pilot_runs",
                column: "executed_at");

            migrationBuilder.CreateIndex(
                name: "IX_production_pilot_runs_run_id",
                schema: "aigateway",
                table: "production_pilot_runs",
                column: "run_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_production_pilot_runs_scenario_id",
                schema: "aigateway",
                table: "production_pilot_runs",
                column: "scenario_id");

            migrationBuilder.CreateIndex(
                name: "IX_production_pilot_runs_source_mode",
                schema: "aigateway",
                table: "production_pilot_runs",
                column: "source_mode");

            migrationBuilder.CreateIndex(
                name: "IX_production_pilot_runs_status",
                schema: "aigateway",
                table: "production_pilot_runs",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_production_pilot_windows_end_at",
                schema: "aigateway",
                table: "production_pilot_windows",
                column: "end_at");

            migrationBuilder.CreateIndex(
                name: "IX_production_pilot_windows_start_at",
                schema: "aigateway",
                table: "production_pilot_windows",
                column: "start_at");

            migrationBuilder.CreateIndex(
                name: "IX_production_pilot_windows_status",
                schema: "aigateway",
                table: "production_pilot_windows",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_production_pilot_windows_window_id",
                schema: "aigateway",
                table: "production_pilot_windows",
                column: "window_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trial_campaigns_created_at",
                schema: "aigateway",
                table: "trial_campaigns",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_trial_campaigns_pilot_readiness_status",
                schema: "aigateway",
                table: "trial_campaigns",
                column: "pilot_readiness_status");

            migrationBuilder.CreateIndex(
                name: "IX_trial_campaigns_status",
                schema: "aigateway",
                table: "trial_campaigns",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_trial_risk_issues_campaign_id",
                schema: "aigateway",
                table: "trial_risk_issues",
                column: "campaign_id");

            migrationBuilder.CreateIndex(
                name: "IX_trial_risk_issues_severity",
                schema: "aigateway",
                table: "trial_risk_issues",
                column: "severity");

            migrationBuilder.CreateIndex(
                name: "IX_trial_risk_issues_status",
                schema: "aigateway",
                table: "trial_risk_issues",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_trial_scenario_runs_campaign_id",
                schema: "aigateway",
                table: "trial_scenario_runs",
                column: "campaign_id");

            migrationBuilder.CreateIndex(
                name: "IX_trial_scenario_runs_source_mode",
                schema: "aigateway",
                table: "trial_scenario_runs",
                column: "source_mode");

            migrationBuilder.CreateIndex(
                name: "IX_trial_scenario_runs_status",
                schema: "aigateway",
                table: "trial_scenario_runs",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_trial_scenario_runs_task_id",
                schema: "aigateway",
                table: "trial_scenario_runs",
                column: "task_id");
        }
    }
}
