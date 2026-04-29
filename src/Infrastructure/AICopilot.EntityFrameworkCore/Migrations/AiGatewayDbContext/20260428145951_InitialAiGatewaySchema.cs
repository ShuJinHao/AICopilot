using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.AiGatewayDbContext
{
    /// <inheritdoc />
    public partial class InitialAiGatewaySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "aigateway");

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF to_regclass('aigateway.language_models') IS NULL
                       AND to_regclass('aigateway.conversation_templates') IS NULL
                       AND to_regclass('aigateway.approval_policies') IS NULL
                       AND to_regclass('aigateway.sessions') IS NULL
                       AND to_regclass('aigateway.messages') IS NULL THEN
                        IF to_regclass('public.language_models') IS NOT NULL
                           OR to_regclass('public.conversation_templates') IS NOT NULL
                           OR to_regclass('public.approval_policies') IS NOT NULL
                           OR to_regclass('public.sessions') IS NOT NULL
                           OR to_regclass('public.messages') IS NOT NULL THEN
                            IF to_regclass('public.language_models') IS NOT NULL THEN
                                ALTER TABLE public.language_models SET SCHEMA aigateway;
                            END IF;

                            IF to_regclass('public.conversation_templates') IS NOT NULL THEN
                                ALTER TABLE public.conversation_templates SET SCHEMA aigateway;
                            END IF;

                            IF to_regclass('public.approval_policies') IS NOT NULL THEN
                                ALTER TABLE public.approval_policies SET SCHEMA aigateway;
                            END IF;

                            IF to_regclass('public.sessions') IS NOT NULL THEN
                                ALTER TABLE public.sessions SET SCHEMA aigateway;
                            END IF;

                            IF to_regclass('public.messages') IS NOT NULL THEN
                                ALTER TABLE public.messages SET SCHEMA aigateway;
                            END IF;
                        ELSE
                            CREATE TABLE aigateway.approval_policies (
                                id uuid NOT NULL,
                                name character varying(200) NOT NULL,
                                description character varying(1000) NULL,
                                target_type character varying(50) NOT NULL,
                                target_name character varying(200) NOT NULL,
                                tool_names text[] NOT NULL,
                                is_enabled boolean NOT NULL,
                                requires_onsite_attestation boolean NOT NULL,
                                CONSTRAINT "PK_approval_policies" PRIMARY KEY (id)
                            );

                            CREATE UNIQUE INDEX "IX_approval_policies_name"
                                ON aigateway.approval_policies (name);

                            CREATE TABLE aigateway.conversation_templates (
                                id uuid NOT NULL,
                                name character varying(200) NOT NULL,
                                description character varying(1000) NOT NULL,
                                system_prompt text NOT NULL,
                                model_id uuid NOT NULL,
                                max_tokens integer NULL,
                                temperature real NULL,
                                is_enabled boolean NOT NULL,
                                CONSTRAINT "PK_conversation_templates" PRIMARY KEY (id)
                            );

                            CREATE UNIQUE INDEX "IX_conversation_templates_name"
                                ON aigateway.conversation_templates (name);

                            CREATE TABLE aigateway.language_models (
                                id uuid NOT NULL,
                                provider character varying(100) NOT NULL,
                                name character varying(100) NOT NULL,
                                base_url character varying(100) NOT NULL,
                                api_key character varying(2048) NULL,
                                max_tokens integer NOT NULL,
                                temperature real NOT NULL,
                                CONSTRAINT "PK_language_models" PRIMARY KEY (id)
                            );

                            CREATE UNIQUE INDEX "IX_language_models_provider_name"
                                ON aigateway.language_models (provider, name);

                            CREATE TABLE aigateway.sessions (
                                id uuid NOT NULL,
                                title character varying(20) NOT NULL,
                                user_id uuid NOT NULL,
                                template_id uuid NOT NULL,
                                onsite_confirmed_at timestamp with time zone NULL,
                                onsite_confirmed_by character varying(256) NULL,
                                onsite_confirmation_expires_at timestamp with time zone NULL,
                                CONSTRAINT "PK_sessions" PRIMARY KEY (id)
                            );

                            CREATE INDEX ix_sessions_user_id
                                ON aigateway.sessions (user_id);

                            CREATE TABLE aigateway.messages (
                                id integer GENERATED BY DEFAULT AS IDENTITY,
                                session_id uuid NOT NULL,
                                content text NOT NULL,
                                created_at timestamp with time zone NOT NULL,
                                type character varying(50) NOT NULL,
                                CONSTRAINT "PK_messages" PRIMARY KEY (id),
                                CONSTRAINT fk_messages_sessions_session_id
                                    FOREIGN KEY (session_id)
                                    REFERENCES aigateway.sessions (id)
                                    ON DELETE CASCADE
                            );

                            CREATE INDEX "IX_messages_session_id"
                                ON aigateway.messages (session_id);
                        END IF;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF to_regclass('aigateway.messages') IS NOT NULL
                       AND to_regclass('public.messages') IS NULL THEN
                        ALTER TABLE aigateway.messages SET SCHEMA public;
                    END IF;

                    IF to_regclass('aigateway.sessions') IS NOT NULL
                       AND to_regclass('public.sessions') IS NULL THEN
                        ALTER TABLE aigateway.sessions SET SCHEMA public;
                    END IF;

                    IF to_regclass('aigateway.approval_policies') IS NOT NULL
                       AND to_regclass('public.approval_policies') IS NULL THEN
                        ALTER TABLE aigateway.approval_policies SET SCHEMA public;
                    END IF;

                    IF to_regclass('aigateway.conversation_templates') IS NOT NULL
                       AND to_regclass('public.conversation_templates') IS NULL THEN
                        ALTER TABLE aigateway.conversation_templates SET SCHEMA public;
                    END IF;

                    IF to_regclass('aigateway.language_models') IS NOT NULL
                       AND to_regclass('public.language_models') IS NULL THEN
                        ALTER TABLE aigateway.language_models SET SCHEMA public;
                    END IF;
                END $$;
                """);
        }
    }
}
