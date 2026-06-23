using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.AiGatewayDbContext
{
    [DbContext(typeof(global::AICopilot.EntityFrameworkCore.AiGatewayDbContext))]
    [Migration("20260622022909_AddMessageRenderPayload")]
    public partial class AddMessageRenderPayload : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "render_payload_json",
                schema: "aigateway",
                table: "messages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "sequence",
                schema: "aigateway",
                table: "messages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                UPDATE aigateway.messages AS message
                SET sequence = ordered.sequence
                FROM (
                    SELECT
                        id,
                        ROW_NUMBER() OVER (
                            PARTITION BY session_id
                            ORDER BY created_at, id
                        ) AS sequence
                    FROM aigateway.messages
                ) AS ordered
                WHERE message.id = ordered.id;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_messages_session_id_sequence",
                schema: "aigateway",
                table: "messages",
                columns: new[] { "session_id", "sequence" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_messages_session_id_sequence",
                schema: "aigateway",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "render_payload_json",
                schema: "aigateway",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "sequence",
                schema: "aigateway",
                table: "messages");
        }
    }
}
