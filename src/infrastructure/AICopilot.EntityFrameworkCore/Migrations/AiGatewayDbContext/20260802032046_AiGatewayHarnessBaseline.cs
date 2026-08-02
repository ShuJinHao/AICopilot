using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.AiGatewayDbContext
{
    /// <inheritdoc />
    public partial class AiGatewayHarnessBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "aigateway");

            migrationBuilder.CreateSequence(
                name: "model_quota_fencing_seq",
                schema: "aigateway");

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
                    message_count = table.Column<int>(type: "integer", nullable: false)
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
                    category = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    business_domains = table.Column<string[]>(type: "text[]", nullable: false),
                    data_boundary = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    is_executable_by_agent = table.Column<bool>(type: "boolean", nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    catalog_version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tool_registrations", x => x.id);
                });

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
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    final_model_id = table.Column<Guid>(type: "uuid", nullable: true),
                    final_model_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    context_window_tokens = table.Column<int>(type: "integer", nullable: true),
                    max_output_tokens = table.Column<int>(type: "integer", nullable: true),
                    render_payload_json = table.Column<string>(type: "text", nullable: true)
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
                name: "ix_agent_session_states_user_expiry",
                schema: "aigateway",
                table: "agent_session_states",
                columns: new[] { "user_id", "expires_at_utc" });

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
                name: "ix_messages_session_id_sequence",
                schema: "aigateway",
                table: "messages",
                columns: new[] { "session_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_model_quota_reservations_authority_window",
                schema: "aigateway",
                table: "model_quota_reservations",
                columns: new[] { "tenant_key_hash", "user_id", "role_key_hash", "window_started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_model_quota_reservations_endpoint_window",
                schema: "aigateway",
                table: "model_quota_reservations",
                columns: new[] { "endpoint_id", "model_id", "window_started_at_utc", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_model_quota_reservations_expiry",
                schema: "aigateway",
                table: "model_quota_reservations",
                columns: new[] { "status", "expires_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_model_quota_reservations_correlation",
                schema: "aigateway",
                table: "model_quota_reservations",
                column: "correlation_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sessions_user_id",
                schema: "aigateway",
                table: "sessions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_tool_registrations_tool_code",
                schema: "aigateway",
                table: "tool_registrations",
                column: "tool_code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_session_states",
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
                name: "model_quota_reservations",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "tool_registrations",
                schema: "aigateway");

            migrationBuilder.DropTable(
                name: "sessions",
                schema: "aigateway");

            migrationBuilder.DropSequence(
                name: "model_quota_fencing_seq",
                schema: "aigateway");
        }
    }
}
