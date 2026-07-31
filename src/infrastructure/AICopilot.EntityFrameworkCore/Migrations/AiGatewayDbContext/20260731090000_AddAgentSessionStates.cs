using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.AiGatewayDbContext;

[DbContext(typeof(AICopilot.EntityFrameworkCore.AiGatewayDbContext))]
[Migration("20260731090000_AddAgentSessionStates")]
public partial class AddAgentSessionStates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "agent_session_states",
            schema: "aigateway",
            columns: table => new
            {
                session_id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: true),
                agent_schema_version = table.Column<int>(type: "integer", nullable: false),
                protected_state = table.Column<string>(type: "text", nullable: false),
                status = table.Column<string>(
                    type: "character varying(32)",
                    maxLength: 32,
                    nullable: false),
                active_turn_id = table.Column<Guid>(type: "uuid", nullable: true),
                version = table.Column<long>(type: "bigint", nullable: false),
                created_at_utc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false),
                updated_at_utc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false),
                expires_at_utc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false),
                protected_approval_bindings = table.Column<string>(
                    type: "text",
                    nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_agent_session_states", row => row.session_id);
                table.ForeignKey(
                    name: "FK_agent_session_states_sessions_session_id",
                    column: row => row.session_id,
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
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "agent_session_states",
            schema: "aigateway");
    }
}
