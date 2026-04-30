using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.DataAnalysisDbContext
{
    [DbContext(typeof(global::AICopilot.EntityFrameworkCore.DataAnalysisDbContext))]
    [Migration("20260427000300_InitialDataAnalysisSchema")]
    public partial class InitialDataAnalysisSchema : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "dataanalysis");

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF to_regclass('dataanalysis.business_databases') IS NULL THEN
                        IF to_regclass('public.business_databases') IS NOT NULL THEN
                            ALTER TABLE public.business_databases SET SCHEMA dataanalysis;
                        ELSE
                            CREATE TABLE dataanalysis.business_databases (
                                id uuid NOT NULL,
                                name character varying(200) NOT NULL,
                                description character varying(1000) NOT NULL,
                                connection_string text NOT NULL,
                                provider character varying(50) NOT NULL,
                                is_read_only boolean NOT NULL,
                                is_enabled boolean NOT NULL,
                                created_at timestamp with time zone NOT NULL,
                                CONSTRAINT "PK_business_databases" PRIMARY KEY (id)
                            );

                            CREATE UNIQUE INDEX "IX_business_databases_name"
                                ON dataanalysis.business_databases (name);
                        END IF;
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
                    IF to_regclass('dataanalysis.business_databases') IS NOT NULL
                       AND to_regclass('public.business_databases') IS NULL THEN
                        ALTER TABLE dataanalysis.business_databases SET SCHEMA public;
                    END IF;
                END $$;
                """);
        }
    }
}
