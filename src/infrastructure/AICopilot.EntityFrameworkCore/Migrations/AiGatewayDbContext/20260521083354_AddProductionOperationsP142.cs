using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.AiGatewayDbContext
{
    /// <inheritdoc />
    public partial class AddProductionOperationsP142 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "production_pilot_emergency_stop_states",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    reason = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    activated_by = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cleared_by = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    cleared_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
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
                    status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    checks_json = table.Column<string>(type: "jsonb", nullable: false),
                    blockers = table.Column<string[]>(type: "text[]", nullable: false),
                    warnings = table.Column<string[]>(type: "text[]", nullable: false),
                    total_runs = table.Column<int>(type: "integer", nullable: false),
                    succeeded_runs = table.Column<int>(type: "integer", nullable: false),
                    failed_runs = table.Column<int>(type: "integer", nullable: false),
                    rejected_runs = table.Column<int>(type: "integer", nullable: false),
                    timeout_runs = table.Column<int>(type: "integer", nullable: false),
                    truncated_runs = table.Column<int>(type: "integer", nullable: false),
                    total_rows = table.Column<int>(type: "integer", nullable: false),
                    final_artifact_count = table.Column<int>(type: "integer", nullable: false),
                    open_incident_count = table.Column<int>(type: "integer", nullable: false),
                    endpoint_distribution_json = table.Column<string>(type: "jsonb", nullable: false),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
                    severity = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    category = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    owner = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    source_ref = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    resolution_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
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
                    run_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_mode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    boundary = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    trial_mode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    pilot_window_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    intent_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    endpoint_code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    artifact_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    approval_status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    duration_ms = table.Column<long>(type: "bigint", nullable: false),
                    row_count = table.Column<int>(type: "integer", nullable: false),
                    is_truncated = table.Column<bool>(type: "boolean", nullable: false),
                    query_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    result_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    executed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_pilot_run_ledgers", x => x.id);
                });

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
        }
    }
}
