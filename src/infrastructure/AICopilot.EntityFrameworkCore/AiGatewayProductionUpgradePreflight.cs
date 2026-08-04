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
        var businessTableCount = await ReadInt32Async(
            connection,
            """
            SELECT count(*)::integer
            FROM information_schema.tables
            WHERE table_schema = 'aigateway'
              AND table_type = 'BASE TABLE'
              AND table_name <> '__EFMigrationsHistory_AiGateway';
            """,
            cancellationToken);

        if (!historyExists && businessTableCount == 0)
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
                   coalesce(numeric_precision::text, '') || '|' || coalesce(numeric_scale::text, '')
            FROM information_schema.columns
            WHERE table_schema = 'aigateway'
              AND table_name <> '__EFMigrationsHistory_AiGateway'
            ORDER BY table_name, ordinal_position;

            SELECT 'index|' || tablename || '|' || indexname
            FROM pg_indexes
            WHERE schemaname = 'aigateway'
              AND tablename <> '__EFMigrationsHistory_AiGateway'
            ORDER BY tablename, indexname;

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
