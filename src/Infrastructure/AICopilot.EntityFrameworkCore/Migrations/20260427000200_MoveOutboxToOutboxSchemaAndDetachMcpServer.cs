using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations
{
    [DbContext(typeof(AiCopilotDbContext))]
    [Migration("20260427000200_MoveOutboxToOutboxSchemaAndDetachMcpServer")]
    public partial class MoveOutboxToOutboxSchemaAndDetachMcpServer : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "outbox");

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF to_regclass('public.outbox_messages') IS NOT NULL
                       AND to_regclass('outbox.outbox_messages') IS NULL THEN
                        ALTER TABLE public.outbox_messages SET SCHEMA outbox;
                    ELSIF to_regclass('public.outbox_messages') IS NOT NULL
                          AND to_regclass('outbox.outbox_messages') IS NOT NULL THEN
                        INSERT INTO outbox.outbox_messages
                        SELECT * FROM public.outbox_messages
                        ON CONFLICT (id) DO NOTHING;

                        DROP TABLE public.outbox_messages;
                    END IF;
                END $$;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF to_regclass('outbox.outbox_messages') IS NOT NULL
                       AND to_regclass('public.outbox_messages') IS NULL THEN
                        ALTER TABLE outbox.outbox_messages SET SCHEMA public;
                    ELSIF to_regclass('outbox.outbox_messages') IS NOT NULL
                          AND to_regclass('public.outbox_messages') IS NOT NULL THEN
                        INSERT INTO public.outbox_messages
                        SELECT * FROM outbox.outbox_messages
                        ON CONFLICT (id) DO NOTHING;

                        DROP TABLE outbox.outbox_messages;
                    END IF;
                END $$;
                """);
        }
    }
}
