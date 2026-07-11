using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.RagDbContext
{
    /// <inheritdoc />
    public partial class CalibrateRagDocumentIdSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("LOCK TABLE rag.documents IN SHARE ROW EXCLUSIVE MODE;");
            migrationBuilder.Sql(
                """
                DO $calibrate$
                DECLARE
                    maximum_document_id bigint;
                    current_sequence_value bigint;
                BEGIN
                    IF to_regclass('rag.documents_id_seq') IS NULL THEN
                        RAISE EXCEPTION 'Required RAG document sequence rag.documents_id_seq does not exist.';
                    END IF;

                    SELECT COALESCE(MAX(id), 0)
                    INTO maximum_document_id
                    FROM rag.documents;

                    IF maximum_document_id > 0 THEN
                        SELECT last_value
                        INTO current_sequence_value
                        FROM rag.documents_id_seq;

                        PERFORM setval(
                            'rag.documents_id_seq',
                            GREATEST(maximum_document_id, current_sequence_value),
                            true);
                    END IF;
                END
                $calibrate$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sequence values are monotonic production state and must never be lowered on rollback.
        }
    }
}
