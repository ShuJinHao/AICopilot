using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace AICopilot.EntityFrameworkCore;

public enum AiGatewayProductionUpgradeState
{
    Fresh,
    ProductionBaseline,
    Current
}

public sealed record AiGatewayProductionUpgradeInspection(
    AiGatewayProductionUpgradeState State,
    string HistorySha256,
    int HistoryLineCount,
    string SchemaSha256,
    int SchemaLineCount);

public static class AiGatewayProductionUpgradePreflight
{
    private const string HistoryTable = "__EFMigrationsHistory_AiGateway";

    public static async Task<AiGatewayProductionUpgradeInspection> InspectAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var historyExists = await ReadBooleanAsync(
            connection,
            "SELECT to_regclass('aigateway.\"__EFMigrationsHistory_AiGateway\"') IS NOT NULL;",
            cancellationToken);
        var schemaObjectCount = await ReadInt32Async(
            connection,
            """
            SELECT (
                (SELECT count(*)
                 FROM pg_class AS object_class
                 JOIN pg_namespace AS object_namespace
                   ON object_namespace.oid = object_class.relnamespace
                 WHERE object_namespace.nspname = 'aigateway') +
                (SELECT count(*)
                 FROM pg_proc AS object_function
                 JOIN pg_namespace AS object_namespace
                   ON object_namespace.oid = object_function.pronamespace
                 WHERE object_namespace.nspname = 'aigateway') +
                (SELECT count(*)
                 FROM pg_type AS object_type
                 JOIN pg_namespace AS object_namespace
                   ON object_namespace.oid = object_type.typnamespace
                 WHERE object_namespace.nspname = 'aigateway')
            )::integer;
            """,
            cancellationToken);

        if (!historyExists && schemaObjectCount == 0)
        {
            return new AiGatewayProductionUpgradeInspection(
                AiGatewayProductionUpgradeState.Fresh,
                Sha256([]),
                0,
                Sha256([]),
                0);
        }

        var historyLines = historyExists
            ? await ReadLinesAsync(
                connection,
                """
                SELECT "MigrationId" || '|' || "ProductVersion"
                FROM aigateway."__EFMigrationsHistory_AiGateway"
                ORDER BY "MigrationId";
                """,
                cancellationToken)
            : [];
        var schemaLines = await ReadLinesAsync(
            connection,
            """
            SELECT 'table|' || table_name
            FROM information_schema.tables
            WHERE table_schema = 'aigateway'
              AND table_type = 'BASE TABLE'
              AND table_name <> '__EFMigrationsHistory_AiGateway'
            ORDER BY table_name;

            SELECT 'column|' || table_name || '|' || lpad(ordinal_position::text, 4, '0') || '|' ||
                   column_name || '|' || data_type || '|' || udt_name || '|' || is_nullable || '|' ||
                   coalesce(character_maximum_length::text, '') || '|' ||
                   coalesce(numeric_precision::text, '') || '|' || coalesce(numeric_scale::text, '') || '|' ||
                   coalesce(column_default, '') || '|' || coalesce(collation_name, '') || '|' ||
                   is_identity || '|' || coalesce(identity_generation, '') || '|' ||
                   is_generated || '|' || coalesce(generation_expression, '')
            FROM information_schema.columns
            WHERE table_schema = 'aigateway'
              AND table_name <> '__EFMigrationsHistory_AiGateway'
            ORDER BY table_name, ordinal_position;

            SELECT 'index|' || table_class.relname || '|' || index_class.relname || '|' ||
                   index_state.indisunique::text || '|' || index_state.indisprimary::text || '|' ||
                   index_state.indisexclusion::text || '|' || index_state.indimmediate::text || '|' ||
                   index_state.indisclustered::text || '|' || index_state.indisvalid::text || '|' ||
                   index_state.indisready::text || '|' || index_state.indislive::text || '|' ||
                   index_state.indisreplident::text || '|' || pg_get_indexdef(index_class.oid)
            FROM pg_index AS index_state
            JOIN pg_class AS table_class ON table_class.oid = index_state.indrelid
            JOIN pg_class AS index_class ON index_class.oid = index_state.indexrelid
            JOIN pg_namespace AS table_namespace ON table_namespace.oid = table_class.relnamespace
            WHERE table_namespace.nspname = 'aigateway'
              AND table_class.relname <> '__EFMigrationsHistory_AiGateway'
            ORDER BY table_class.relname, index_class.relname;

            SELECT 'constraint|' || table_class.relname || '|' || constraint_state.conname || '|' ||
                   constraint_state.contype::text || '|' || constraint_state.condeferrable::text || '|' ||
                   constraint_state.condeferred::text || '|' || constraint_state.convalidated::text || '|' ||
                   pg_get_constraintdef(constraint_state.oid, true)
            FROM pg_constraint AS constraint_state
            JOIN pg_class AS table_class ON table_class.oid = constraint_state.conrelid
            JOIN pg_namespace AS table_namespace ON table_namespace.oid = table_class.relnamespace
            WHERE table_namespace.nspname = 'aigateway'
              AND table_class.relname <> '__EFMigrationsHistory_AiGateway'
            ORDER BY table_class.relname, constraint_state.conname;

            SELECT 'table-security|' || table_class.relname || '|' ||
                   table_class.relrowsecurity::text || '|' ||
                   table_class.relforcerowsecurity::text || '|' ||
                   table_class.relreplident::text
            FROM pg_class AS table_class
            JOIN pg_namespace AS table_namespace ON table_namespace.oid = table_class.relnamespace
            WHERE table_namespace.nspname = 'aigateway'
              AND table_class.relkind IN ('r', 'p')
              AND table_class.relname <> '__EFMigrationsHistory_AiGateway'
            ORDER BY table_class.relname;

            SELECT 'trigger|' || table_class.relname || '|' || trigger_state.tgname || '|' ||
                   trigger_state.tgenabled::text || '|' || pg_get_triggerdef(trigger_state.oid, true)
            FROM pg_trigger AS trigger_state
            JOIN pg_class AS table_class ON table_class.oid = trigger_state.tgrelid
            JOIN pg_namespace AS table_namespace ON table_namespace.oid = table_class.relnamespace
            WHERE table_namespace.nspname = 'aigateway'
              AND table_class.relname <> '__EFMigrationsHistory_AiGateway'
              AND NOT trigger_state.tgisinternal
            ORDER BY table_class.relname, trigger_state.tgname;

            SELECT 'trigger-function|' || function_namespace.nspname || '|' ||
                   function_state.proname || '|' ||
                   pg_get_function_identity_arguments(function_state.oid) || '|' ||
                   pg_get_functiondef(function_state.oid)
            FROM pg_proc AS function_state
            JOIN pg_namespace AS function_namespace ON function_namespace.oid = function_state.pronamespace
            WHERE function_state.oid IN (
                SELECT DISTINCT trigger_state.tgfoid
                FROM pg_trigger AS trigger_state
                JOIN pg_class AS table_class ON table_class.oid = trigger_state.tgrelid
                JOIN pg_namespace AS table_namespace ON table_namespace.oid = table_class.relnamespace
                WHERE table_namespace.nspname = 'aigateway'
                  AND table_class.relname <> '__EFMigrationsHistory_AiGateway'
                  AND NOT trigger_state.tgisinternal)
            ORDER BY function_namespace.nspname, function_state.proname,
                     pg_get_function_identity_arguments(function_state.oid);

            SELECT 'policy|' || table_class.relname || '|' || policy_state.polname || '|' ||
                   policy_state.polpermissive::text || '|' || policy_state.polcmd::text || '|' ||
                   coalesce((
                       SELECT string_agg(policy_role.role_name, ',' ORDER BY policy_role.role_name)
                       FROM (
                           SELECT CASE WHEN role_oid = 0
                                       THEN 'public'
                                       ELSE pg_get_userbyid(role_oid)
                                  END AS role_name
                           FROM unnest(policy_state.polroles) AS expanded_role(role_oid)
                       ) AS policy_role), '') || '|' ||
                   coalesce(pg_get_expr(policy_state.polqual, policy_state.polrelid, true), '') || '|' ||
                   coalesce(pg_get_expr(policy_state.polwithcheck, policy_state.polrelid, true), '')
            FROM pg_policy AS policy_state
            JOIN pg_class AS table_class ON table_class.oid = policy_state.polrelid
            JOIN pg_namespace AS table_namespace ON table_namespace.oid = table_class.relnamespace
            WHERE table_namespace.nspname = 'aigateway'
              AND table_class.relname <> '__EFMigrationsHistory_AiGateway'
            ORDER BY table_class.relname, policy_state.polname;

            SELECT 'sequence|' || sequence_name || '|' || data_type || '|' ||
                   start_value || '|' || minimum_value || '|' || maximum_value || '|' ||
                   increment || '|' || cycle_option
            FROM information_schema.sequences
            WHERE sequence_schema = 'aigateway'
            ORDER BY sequence_name;
            """,
            cancellationToken);

        var historySha = Sha256(historyLines);
        var schemaSha = Sha256(schemaLines);
        var productionHistory = ExpectedHistoryLines(includeCurrent: false);
        var currentHistory = ExpectedHistoryLines(includeCurrent: true);

        if (historyLines.SequenceEqual(productionHistory, StringComparer.Ordinal) &&
            historySha == AiGatewayProductionUpgradeContract.ExpectedProductionHistorySha256 &&
            schemaSha == AiGatewayProductionUpgradeContract.ExpectedProductionSchemaSha256)
        {
            return new AiGatewayProductionUpgradeInspection(
                AiGatewayProductionUpgradeState.ProductionBaseline,
                historySha,
                historyLines.Count,
                schemaSha,
                schemaLines.Count);
        }

        if (historyLines.SequenceEqual(currentHistory, StringComparer.Ordinal) &&
            schemaSha == AiGatewayProductionUpgradeContract.ExpectedCurrentSchemaSha256)
        {
            return new AiGatewayProductionUpgradeInspection(
                AiGatewayProductionUpgradeState.Current,
                historySha,
                historyLines.Count,
                schemaSha,
                schemaLines.Count);
        }

        throw new InvalidOperationException(
            "AiGateway production migration preflight rejected an unknown schema/history state. " +
            $"historySha256={historySha} historyLines={historyLines.Count} " +
            $"schemaSha256={schemaSha} schemaLines={schemaLines.Count}. " +
            "Do not infer or insert migration history.");
    }

    private static string[] ExpectedHistoryLines(bool includeCurrent)
    {
        var ids = includeCurrent
            ? AiGatewayProductionUpgradeContract.ProductionMigrationIds
                .Append(AiGatewayProductionUpgradeContract.CurrentUpgradeMigrationId)
            : AiGatewayProductionUpgradeContract.ProductionMigrationIds;
        return ids
            .Select(id => $"{id}|{AiGatewayProductionUpgradeContract.EfProductVersion}")
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task<List<string>> ReadLinesAsync(
        NpgsqlConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var lines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        do
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                lines.Add(reader.GetString(0));
            }
        }
        while (await reader.NextResultAsync(cancellationToken));
        return lines;
    }

    private static async Task<bool> ReadBooleanAsync(
        NpgsqlConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (bool)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("AiGateway preflight returned no boolean result."));
    }

    private static async Task<int> ReadInt32Async(
        NpgsqlConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (int)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("AiGateway preflight returned no count."));
    }

    private static string Sha256(IReadOnlyCollection<string> lines)
    {
        var canonical = lines.Count == 0 ? string.Empty : string.Join('\n', lines) + "\n";
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }
}
