using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.AiGatewayDbContext
{
    /// <inheritdoc />
    public partial class AiGatewayFreshBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "aigateway");

            migrationBuilder.Sql(
                """
                DROP TABLE IF EXISTS aigateway.agent_steps CASCADE;
                DROP TABLE IF EXISTS aigateway.tool_execution_records CASCADE;
                DROP TABLE IF EXISTS aigateway.agent_worker_heartbeats CASCADE;
                DROP TABLE IF EXISTS aigateway.agent_task_run_queue_items CASCADE;
                DROP TABLE IF EXISTS aigateway.agent_task_run_attempts CASCADE;
                DROP TABLE IF EXISTS aigateway.artifacts CASCADE;
                DROP TABLE IF EXISTS aigateway.approval_policies CASCADE;
                DROP TABLE IF EXISTS aigateway.approval_requests CASCADE;
                DROP TABLE IF EXISTS aigateway.chat_runtime_settings CASCADE;
                DROP TABLE IF EXISTS aigateway.conversation_templates CASCADE;
                DROP TABLE IF EXISTS aigateway.language_models CASCADE;
                DROP TABLE IF EXISTS aigateway.messages CASCADE;
                DROP TABLE IF EXISTS aigateway.routing_model_configurations CASCADE;
                DROP TABLE IF EXISTS aigateway.tool_registrations CASCADE;
                DROP TABLE IF EXISTS aigateway.upload_records CASCADE;
                DROP TABLE IF EXISTS aigateway.agent_tasks CASCADE;
                DROP TABLE IF EXISTS aigateway.artifact_workspaces CASCADE;
                DROP TABLE IF EXISTS aigateway.sessions CASCADE;
                DROP TABLE IF EXISTS public.messages CASCADE;
                DROP TABLE IF EXISTS public.sessions CASCADE;
                DROP TABLE IF EXISTS public.approval_policies CASCADE;
                DROP TABLE IF EXISTS public.conversation_templates CASCADE;
                DROP TABLE IF EXISTS public.language_models CASCADE;
                """);

            migrationBuilder.CreateTable(
                name: "agent_tasks",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    active_run_attempt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    run_attempt_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    run_lease_id = table.Column<Guid>(type: "uuid", nullable: true),
                    run_lease_owner = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    run_lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    plan_json = table.Column<string>(type: "text", nullable: false),
                    final_summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_tasks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "agent_task_run_attempts",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_no = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    trigger_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lease_owner = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    safe_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_task_run_attempts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "agent_task_run_queue_items",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trigger_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                    run_attempt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lease_owner = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    available_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    safe_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_task_run_queue_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "agent_worker_heartbeats",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    worker_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    worker_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    active_queue_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    active_task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    workspace_root_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    version = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_worker_heartbeats", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "approval_policies",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    target_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    target_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tool_names = table.Column<string[]>(type: "text[]", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    requires_onsite_attestation = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_policies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "approval_requests",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approval_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    target_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    approval_comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "artifact_workspaces",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    root_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    workspace_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_artifact_workspaces", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chat_runtime_settings",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    routing_history_count = table.Column<int>(type: "integer", nullable: false),
                    answer_history_count = table.Column<int>(type: "integer", nullable: false),
                    rag_rewrite_history_count = table.Column<int>(type: "integer", nullable: false),
                    agent_planning_history_count = table.Column<int>(type: "integer", nullable: false),
                    summary_threshold_messages = table.Column<int>(type: "integer", nullable: false),
                    context_token_limit = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_runtime_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "conversation_templates",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    system_prompt = table.Column<string>(type: "text", nullable: false),
                    model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    built_in_version = table.Column<int>(type: "integer", nullable: false),
                    is_built_in = table.Column<bool>(type: "boolean", nullable: false),
                    max_tokens = table.Column<int>(type: "integer", nullable: true),
                    temperature = table.Column<float>(type: "real", nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversation_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "language_models",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    protocol_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    base_url = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    api_key = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    max_tokens = table.Column<int>(type: "integer", nullable: false),
                    max_output_tokens = table.Column<int>(type: "integer", nullable: false),
                    temperature = table.Column<float>(type: "real", nullable: false),
                    usage = table.Column<int>(type: "integer", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    connectivity_status = table.Column<int>(type: "integer", nullable: false),
                    connectivity_checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    connectivity_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_language_models", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "routing_model_configurations",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_routing_model_configurations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_message_summary = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    last_message_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    message_count = table.Column<int>(type: "integer", nullable: false),
                    onsite_confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    onsite_confirmed_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    onsite_confirmation_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tool_registrations",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tool_code = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    display_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    provider_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    target_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    target_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    input_schema_json = table.Column<string>(type: "jsonb", nullable: false),
                    output_schema_json = table.Column<string>(type: "jsonb", nullable: false),
                    risk_level = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    required_permission = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    requires_approval = table.Column<bool>(type: "boolean", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    timeout_seconds = table.Column<int>(type: "integer", nullable: false),
                    audit_level = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tool_registrations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "upload_records",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    agent_task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    knowledge_base_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rag_document_id = table.Column<int>(type: "integer", nullable: true),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    storage_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_upload_records", x => x.id);
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
                name: "tool_execution_records",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_attempt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tool_code = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    input_summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    output_summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    duration_ms = table.Column<long>(type: "bigint", nullable: true),
                    error_code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    artifact_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    audit_metadata = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tool_execution_records", x => x.id);
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

            migrationBuilder.CreateTable(
                name: "messages",
                schema: "aigateway",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    final_model_id = table.Column<Guid>(type: "uuid", nullable: true),
                    final_model_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    routing_model_id = table.Column<Guid>(type: "uuid", nullable: true),
                    routing_model_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    context_window_tokens = table.Column<int>(type: "integer", nullable: true),
                    max_output_tokens = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_messages_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "aigateway",
                        principalTable: "sessions",
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
                name: "ix_agent_task_run_attempts_task_attempt_no",
                schema: "aigateway",
                table: "agent_task_run_attempts",
                columns: new[] { "task_id", "attempt_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_agent_task_run_attempts_task_id",
                schema: "aigateway",
                table: "agent_task_run_attempts",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_task_run_queue_items_lease_expires_at",
                schema: "aigateway",
                table: "agent_task_run_queue_items",
                column: "lease_expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_agent_task_run_queue_items_run_attempt_id",
                schema: "aigateway",
                table: "agent_task_run_queue_items",
                column: "run_attempt_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_task_run_queue_items_status_available_at",
                schema: "aigateway",
                table: "agent_task_run_queue_items",
                columns: new[] { "status", "available_at" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_task_run_queue_items_task_id",
                schema: "aigateway",
                table: "agent_task_run_queue_items",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ux_agent_task_run_queue_items_active_task",
                schema: "aigateway",
                table: "agent_task_run_queue_items",
                column: "task_id",
                unique: true,
                filter: "status IN ('Queued', 'Leased')");

            migrationBuilder.CreateIndex(
                name: "ix_agent_worker_heartbeats_active_task_id",
                schema: "aigateway",
                table: "agent_worker_heartbeats",
                column: "active_task_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_worker_heartbeats_last_seen_at",
                schema: "aigateway",
                table: "agent_worker_heartbeats",
                column: "last_seen_at");

            migrationBuilder.CreateIndex(
                name: "ux_agent_worker_heartbeats_worker_id",
                schema: "aigateway",
                table: "agent_worker_heartbeats",
                column: "worker_id",
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
                name: "IX_approval_policies_name",
                schema: "aigateway",
                table: "approval_policies",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_approval_requests_task_id",
                schema: "aigateway",
                table: "approval_requests",
                column: "task_id");

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
                name: "IX_conversation_templates_code",
                schema: "aigateway",
                table: "conversation_templates",
                column: "code",
                unique: true,
                filter: "code IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_conversation_templates_name",
                schema: "aigateway",
                table: "conversation_templates",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_language_models_provider_name",
                schema: "aigateway",
                table: "language_models",
                columns: new[] { "provider", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_messages_session_id",
                schema: "aigateway",
                table: "messages",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "IX_routing_model_configurations_is_active",
                schema: "aigateway",
                table: "routing_model_configurations",
                column: "is_active",
                unique: true,
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "IX_routing_model_configurations_model_id",
                schema: "aigateway",
                table: "routing_model_configurations",
                column: "model_id");

            migrationBuilder.CreateIndex(
                name: "IX_routing_model_configurations_name",
                schema: "aigateway",
                table: "routing_model_configurations",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sessions_user_id",
                schema: "aigateway",
                table: "sessions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_tool_execution_records_task_id",
                schema: "aigateway",
                table: "tool_execution_records",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_tool_execution_records_run_attempt_id",
                schema: "aigateway",
                table: "tool_execution_records",
                column: "run_attempt_id");

            migrationBuilder.CreateIndex(
                name: "ix_tool_execution_records_task_step",
                schema: "aigateway",
                table: "tool_execution_records",
                columns: new[] { "task_id", "step_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tool_execution_records_tool_code",
                schema: "aigateway",
                table: "tool_execution_records",
                column: "tool_code");

            migrationBuilder.CreateIndex(
                name: "IX_tool_registrations_tool_code",
                schema: "aigateway",
                table: "tool_registrations",
                column: "tool_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_upload_records_knowledge_base_id",
                schema: "aigateway",
                table: "upload_records",
                column: "knowledge_base_id");

            migrationBuilder.CreateIndex(
                name: "ix_upload_records_user_agent_task",
                schema: "aigateway",
                table: "upload_records",
                columns: new[] { "user_id", "agent_task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_upload_records_user_session",
                schema: "aigateway",
                table: "upload_records",
                columns: new[] { "user_id", "session_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_steps",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "approval_policies",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "approval_requests",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "tool_execution_records",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "agent_worker_heartbeats",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "agent_task_run_queue_items",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "agent_task_run_attempts",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "artifacts",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "chat_runtime_settings",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "conversation_templates",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "language_models",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "messages",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "routing_model_configurations",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "tool_registrations",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "upload_records",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "agent_tasks",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "artifact_workspaces",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "sessions",
                schema: "aigateway");
        }
    }
}
