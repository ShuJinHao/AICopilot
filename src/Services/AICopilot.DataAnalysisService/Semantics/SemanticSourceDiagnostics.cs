using AICopilot.Services.Contracts;

namespace AICopilot.DataAnalysisService.Semantics;

public static class SemanticSourceStatusValues
{
    public const string Ready = "Ready";
    public const string MissingMapping = "MissingMapping";
    public const string DatabaseNotFound = "DatabaseNotFound";
    public const string Disabled = "Disabled";
    public const string NotReadOnly = "NotReadOnly";
    public const string SourceNotFound = "SourceNotFound";
    public const string ProviderMismatch = "ProviderMismatch";
    public const string FieldMismatch = "FieldMismatch";
}

public static class SemanticSourceContractCatalog
{
    private static readonly IReadOnlyDictionary<SemanticQueryTarget, IReadOnlyList<string>> RequiredFields =
        new Dictionary<SemanticQueryTarget, IReadOnlyList<string>>
        {
            [SemanticQueryTarget.Device] =
            [
                "deviceId", "deviceCode", "deviceName", "status", "lineName", "updatedAt"
            ],
            [SemanticQueryTarget.DeviceLog] =
            [
                "logId", "deviceId", "deviceCode", "level", "message", "source", "occurredAt"
            ],
            [SemanticQueryTarget.Recipe] =
            [
                "recipeId", "recipeName", "deviceId", "deviceCode", "processName", "version", "isActive", "updatedAt"
            ],
            [SemanticQueryTarget.Capacity] =
            [
                "recordId", "deviceId", "deviceCode", "processName", "shiftDate", "outputQty", "qualifiedQty", "occurredAt"
            ],
            [SemanticQueryTarget.ProductionData] =
            [
                "recordId", "deviceId", "deviceCode", "processName", "barcode", "stationName", "result", "occurredAt"
            ]
        };

    public static IReadOnlyList<string> GetRequiredFields(SemanticQueryTarget target)
    {
        return RequiredFields.TryGetValue(target, out var fields)
            ? fields
            : [];
    }
}

public sealed record SemanticSourceInspection(
    bool SourceExists,
    IReadOnlyList<string> MissingRequiredFields);

public interface ISemanticSourceInspector
{
    Task<SemanticSourceInspection> InspectAsync(
        BusinessDatabaseConnectionInfo database,
        SemanticPhysicalMapping mapping,
        CancellationToken cancellationToken = default);
}

public sealed class SemanticSourceInspector(IDatabaseConnector databaseConnector) : ISemanticSourceInspector
{
    public async Task<SemanticSourceInspection> InspectAsync(
        BusinessDatabaseConnectionInfo database,
        SemanticPhysicalMapping mapping,
        CancellationToken cancellationToken = default)
    {
        var effectiveSourceName = ResolveEffectiveSourceName(mapping);
        if (string.IsNullOrWhiteSpace(effectiveSourceName))
        {
            return new SemanticSourceInspection(false, SemanticSourceContractCatalog.GetRequiredFields(mapping.Target));
        }

        var sourceExists = await ProbeSourceExistsAsync(
            database,
            mapping.Provider,
            effectiveSourceName,
            cancellationToken);

        if (!sourceExists)
        {
            return new SemanticSourceInspection(false, []);
        }

        var missingRequiredFields = await GetMissingRequiredFieldsAsync(
            database,
            mapping,
            effectiveSourceName,
            cancellationToken);

        return new SemanticSourceInspection(true, missingRequiredFields);
    }

    public static string ResolveEffectiveSourceName(SemanticPhysicalMapping mapping)
    {
        return string.IsNullOrWhiteSpace(mapping.FromClause)
            ? mapping.SourceName
            : mapping.FromClause;
    }

    private async Task<bool> ProbeSourceExistsAsync(
        BusinessDatabaseConnectionInfo database,
        DatabaseProviderType provider,
        string sourceExpression,
        CancellationToken cancellationToken)
    {
        try
        {
            await databaseConnector.ExecuteQueryAsync(
                database,
                BuildSourceProbeSql(provider, sourceExpression),
                cancellationToken: cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<IReadOnlyList<string>> GetMissingRequiredFieldsAsync(
        BusinessDatabaseConnectionInfo database,
        SemanticPhysicalMapping mapping,
        string sourceExpression,
        CancellationToken cancellationToken)
    {
        var requiredFields = SemanticSourceContractCatalog.GetRequiredFields(mapping.Target);
        if (requiredFields.Count == 0)
        {
            return [];
        }

        try
        {
            await databaseConnector.ExecuteQueryAsync(
                database,
                BuildProjectionProbeSql(mapping.Provider, sourceExpression, mapping, requiredFields),
                cancellationToken: cancellationToken);
            return [];
        }
        catch
        {
            // The combined projection failed. Probe field by field so we can report
            // exactly which semantic fields are missing from the future readonly contract.
        }

        var missingFields = new List<string>();
        foreach (var field in requiredFields)
        {
            if (!mapping.FieldMappings.TryGetValue(field, out var physicalExpression) ||
                string.IsNullOrWhiteSpace(physicalExpression))
            {
                missingFields.Add(field);
                continue;
            }

            try
            {
                await databaseConnector.ExecuteQueryAsync(
                    database,
                    BuildFieldProbeSql(mapping.Provider, sourceExpression, physicalExpression, field),
                    cancellationToken: cancellationToken);
            }
            catch
            {
                missingFields.Add(field);
            }
        }

        return missingFields;
    }

    private static string BuildSourceProbeSql(DatabaseProviderType provider, string sourceExpression)
    {
        return provider switch
        {
            DatabaseProviderType.SqlServer => $"SELECT TOP 0 1 AS readinessProbe FROM {sourceExpression}",
            _ => $"SELECT 1 AS readinessProbe FROM {sourceExpression} LIMIT 0"
        };
    }

    private static string BuildProjectionProbeSql(
        DatabaseProviderType provider,
        string sourceExpression,
        SemanticPhysicalMapping mapping,
        IReadOnlyList<string> requiredFields)
    {
        var projections = requiredFields
            .Where(field => mapping.FieldMappings.TryGetValue(field, out var expression) && !string.IsNullOrWhiteSpace(expression))
            .Select(field => $"{mapping.FieldMappings[field]} AS {field}")
            .ToArray();

        if (projections.Length == 0)
        {
            return BuildSourceProbeSql(provider, sourceExpression);
        }

        return BuildSelectProbeSql(provider, sourceExpression, string.Join(", ", projections));
    }

    private static string BuildFieldProbeSql(
        DatabaseProviderType provider,
        string sourceExpression,
        string physicalExpression,
        string semanticField)
    {
        return BuildSelectProbeSql(provider, sourceExpression, $"{physicalExpression} AS {semanticField}");
    }

    private static string BuildSelectProbeSql(
        DatabaseProviderType provider,
        string sourceExpression,
        string projectionSql)
    {
        return provider switch
        {
            DatabaseProviderType.SqlServer => $"SELECT TOP 0 {projectionSql} FROM {sourceExpression}",
            _ => $"SELECT {projectionSql} FROM {sourceExpression} LIMIT 0"
        };
    }
}


