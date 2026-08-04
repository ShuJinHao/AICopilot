using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.AiGatewayDbContext
{
    /// <inheritdoc />
    public partial class AddTrialOperationsP10 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trial_campaigns",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    allowed_source_modes = table.Column<string[]>(type: "text[]", nullable: false),
                    owner_department = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    pilot_readiness_status = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
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
                    severity = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    category = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    owner = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    source_ref = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    resolution_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scenario_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    trial_mode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    source_mode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    boundary = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    artifact_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    query_hashes = table.Column<string[]>(type: "text[]", nullable: false),
                    result_hashes = table.Column<string[]>(type: "text[]", nullable: false),
                    approval_status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
    }
}
