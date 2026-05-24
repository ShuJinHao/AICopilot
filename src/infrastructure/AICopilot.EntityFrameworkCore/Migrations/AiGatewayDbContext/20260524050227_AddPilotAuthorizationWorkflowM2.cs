using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.AiGatewayDbContext
{
    /// <inheritdoc />
    public partial class AddPilotAuthorizationWorkflowM2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pilot_authorization_submissions",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_user_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    business_purpose = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    endpoint_codes = table.Column<string[]>(type: "text[]", nullable: false),
                    max_rows = table.Column<int>(type: "integer", nullable: false),
                    time_range_days = table.Column<int>(type: "integer", nullable: false),
                    data_owner = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    tool_owner = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    final_owner = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    machine_validation_status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    machine_rejected_reasons = table.Column<string[]>(type: "text[]", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_reviewer_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_reviewer_user_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    last_decision_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    last_decision_status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    last_decision_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    credential_window_planning_summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    credential_window_planning_approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rollback_owner = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    emergency_owner = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    rollback_summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    evidence_summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    evidence_artifact_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pilot_authorization_submissions", x => x.id);
                });

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pilot_authorization_submissions",
                schema: "aigateway");
        }
    }
}
