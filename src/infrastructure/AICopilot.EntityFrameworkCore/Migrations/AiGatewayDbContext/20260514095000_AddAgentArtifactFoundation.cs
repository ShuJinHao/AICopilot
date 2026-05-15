using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.AiGatewayDbContext
{
    /// <inheritdoc />
    [DbContext(typeof(EntityFrameworkCore.AiGatewayDbContext))]
    [Migration("20260514095000_AddAgentArtifactFoundation")]
    public partial class AddAgentArtifactFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_tasks",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    task_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    goal = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    task_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    risk_level = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    model_id = table.Column<Guid>(type: "uuid", nullable: true),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: true),
                    plan_json = table.Column<string>(type: "text", nullable: false),
                    final_summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_tasks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "artifact_workspaces",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    root_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    workspace_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_artifact_workspaces", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "approval_requests",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approval_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    target_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    approval_comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "agent_steps",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_index = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    step_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    tool_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    requires_approval = table.Column<bool>(type: "boolean", nullable: false),
                    input_json = table.Column<string>(type: "text", nullable: true),
                    output_json = table.Column<string>(type: "text", nullable: true),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_steps", x => x.id);
                    table.ForeignKey(
                        name: "FK_agent_steps_agent_tasks_task_id",
                        column: x => x.task_id,
                        principalSchema: "aigateway",
                        principalTable: "agent_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "artifacts",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    artifact_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    relative_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    mime_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_by_step_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_artifacts", x => x.id);
                    table.ForeignKey(
                        name: "FK_artifacts_artifact_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalSchema: "aigateway",
                        principalTable: "artifact_workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_steps_task_id_step_index",
                schema: "aigateway",
                table: "agent_steps",
                columns: new[] { "task_id", "step_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agent_tasks_task_code",
                schema: "aigateway",
                table: "agent_tasks",
                column: "task_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_agent_tasks_user_id",
                schema: "aigateway",
                table: "agent_tasks",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_artifact_workspaces_task_id",
                schema: "aigateway",
                table: "artifact_workspaces",
                column: "task_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_artifact_workspaces_workspace_code",
                schema: "aigateway",
                table: "artifact_workspaces",
                column: "workspace_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_artifacts_workspace_id_relative_path",
                schema: "aigateway",
                table: "artifacts",
                columns: new[] { "workspace_id", "relative_path" });

            migrationBuilder.CreateIndex(
                name: "ix_approval_requests_task_id",
                schema: "aigateway",
                table: "approval_requests",
                column: "task_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_steps",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "approval_requests",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "artifacts",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "agent_tasks",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "artifact_workspaces",
                schema: "aigateway");
        }
    }
}
