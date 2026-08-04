using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.AiGatewayDbContext
{
    /// <inheritdoc />
    public partial class AddProductionPilotHardeningP160 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "production_controlled_pilot_intents",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    intent_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    goal_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    endpoint_codes = table.Column<string[]>(type: "text[]", nullable: false),
                    time_range_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    time_range_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    max_rows = table.Column<int>(type: "integer", nullable: false),
                    artifact_types = table.Column<string[]>(type: "text[]", nullable: false),
                    analysis_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    warnings = table.Column<string[]>(type: "text[]", nullable: false),
                    rejected_reasons = table.Column<string[]>(type: "text[]", nullable: false),
                    requires_tool_approval = table.Column<bool>(type: "boolean", nullable: false),
                    requires_final_approval = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
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
                    run_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    intent_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    analysis_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    endpoint_code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    source_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    source_mode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    is_production_data = table.Column<bool>(type: "boolean", nullable: false),
                    is_sandbox = table.Column<bool>(type: "boolean", nullable: false),
                    is_simulation = table.Column<bool>(type: "boolean", nullable: false),
                    source_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    boundary = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    pilot_window_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    query_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    result_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    row_count = table.Column<int>(type: "integer", nullable: false),
                    is_truncated = table.Column<bool>(type: "boolean", nullable: false),
                    executed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duration_ms = table.Column<long>(type: "bigint", nullable: false),
                    approval_status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    artifact_types = table.Column<string[]>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_controlled_pilot_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "production_pilot_runs",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    scenario_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    scenario_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    endpoint_code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    source_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    source_mode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    is_production_data = table.Column<bool>(type: "boolean", nullable: false),
                    is_sandbox = table.Column<bool>(type: "boolean", nullable: false),
                    is_simulation = table.Column<bool>(type: "boolean", nullable: false),
                    source_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    boundary = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    pilot_window_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    query_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    result_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    row_count = table.Column<int>(type: "integer", nullable: false),
                    is_truncated = table.Column<bool>(type: "boolean", nullable: false),
                    executed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duration_ms = table.Column<long>(type: "bigint", nullable: false),
                    approval_status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    artifact_types = table.Column<string[]>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
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
                    window_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    allowed_endpoint_codes = table.Column<string[]>(type: "text[]", nullable: false),
                    max_time_range_days = table.Column<int>(type: "integer", nullable: false),
                    max_rows = table.Column<int>(type: "integer", nullable: false),
                    timeout_ms = table.Column<int>(type: "integer", nullable: false),
                    owner_department = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    approval_policy = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    rollback_policy = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_pilot_windows", x => x.id);
                });

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "production_controlled_pilot_intents",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "production_controlled_pilot_runs",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "production_pilot_runs",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "production_pilot_windows",
                schema: "aigateway");
        }
    }
}
