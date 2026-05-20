using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Core.DataAnalysis.Ids;
using AICopilot.Core.DataAnalysis.Specifications.BusinessDatabase;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.DataAnalysisService.BusinessDatabases;

[AuthorizeRequirement("DataSource.Query")]
public sealed record ExecuteBusinessDatabaseReadonlyQueryCommand(
    Guid DataSourceId,
    string Sql,
    int? Limit = null) : ICommand<Result<BusinessQueryResultDto>>;

public sealed class ExecuteBusinessDatabaseReadonlyQueryCommandHandler(
    BusinessReadonlyQueryExecutor executor)
    : ICommandHandler<ExecuteBusinessDatabaseReadonlyQueryCommand, Result<BusinessQueryResultDto>>
{
    public Task<Result<BusinessQueryResultDto>> Handle(
        ExecuteBusinessDatabaseReadonlyQueryCommand request,
        CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync(
            request.DataSourceId,
            request.Sql,
            request.Limit,
            requireSimulationBusiness: false,
            safetySchema: null,
            auditAction: "DataSource.Query",
            cancellationToken);
    }
}

public sealed class BusinessReadonlyQueryExecutor(
    IReadRepository<BusinessDatabase> repository,
    IDatabaseConnector databaseConnector,
    IAuditLogWriter auditLogWriter)
{
    public async Task<Result<BusinessQueryResultDto>> ExecuteAsync(
        Guid dataSourceId,
        string sql,
        int? limit,
        bool requireSimulationBusiness,
        BusinessQuerySafetySchema? safetySchema,
        string auditAction,
        CancellationToken cancellationToken)
    {
        var database = await repository.FirstOrDefaultAsync(
            new BusinessDatabaseByIdSpec(new BusinessDatabaseId(dataSourceId)),
            cancellationToken);
        if (database is null)
        {
            return Result.NotFound();
        }

        if (!database.IsEnabled || !database.IsReadOnly)
        {
            await WriteAuditAsync(
                database,
                sql,
                AuditResults.Rejected,
                "Business readonly query rejected because the data source is disabled or not readonly.",
                rowCount: 0,
                isTruncated: false,
                durationMs: 0,
                auditAction,
                cancellationToken);
            return Result.Invalid("Business database is disabled or not readonly.");
        }

        if (requireSimulationBusiness &&
            database.ExternalSystemType != BusinessDataExternalSystemType.SimulationBusiness)
        {
            await WriteAuditAsync(
                database,
                sql,
                AuditResults.Rejected,
                "Business Text-to-SQL rejected because P1 only executes SimulationBusiness data sources.",
                rowCount: 0,
                isTruncated: false,
                durationMs: 0,
                auditAction,
                cancellationToken);
            return Result.Invalid("P1 Text-to-SQL only executes SimulationBusiness data sources.");
        }

        var safetyError = BusinessReadonlyQuerySafetyPolicy.Validate(sql, safetySchema);
        if (safetyError is not null)
        {
            await WriteAuditAsync(
                database,
                sql,
                AuditResults.Rejected,
                $"Business readonly query rejected. Reason={safetyError}",
                rowCount: 0,
                isTruncated: false,
                durationMs: 0,
                auditAction,
                cancellationToken);
            return Result.Invalid(safetyError);
        }

        var maxRows = ResolveLimit(database, limit);
        var queryOptions = new DatabaseQueryOptions(
            MaxRows: maxRows,
            CommandTimeoutSeconds: 15);

        try
        {
            var queryResult = await databaseConnector.ExecuteQueryWithMetadataAsync(
                BusinessDatabaseContractMapper.ToConnectionInfo(database),
                sql,
                options: queryOptions,
                cancellationToken: cancellationToken);

            var dto = BusinessQueryResultMapper.Map(database, sql, queryResult);
            await WriteAuditAsync(
                database,
                sql,
                AuditResults.Succeeded,
                $"Business readonly query executed. RowsObserved={queryResult.ReturnedRowCount}; Truncated={queryResult.IsTruncated}; DurationMs={queryResult.ElapsedMilliseconds}.",
                queryResult.ReturnedRowCount,
                queryResult.IsTruncated,
                queryResult.ElapsedMilliseconds,
                auditAction,
                cancellationToken);

            return Result.Success(dto);
        }
        catch (InvalidOperationException ex)
        {
            await WriteAuditAsync(
                database,
                sql,
                AuditResults.Rejected,
                $"Business readonly query rejected by runtime guard. ErrorType={ex.GetType().Name}.",
                rowCount: 0,
                isTruncated: false,
                durationMs: 0,
                auditAction,
                cancellationToken);
            return Result.Invalid("Business readonly query was rejected by the SQL guardrail.");
        }
        catch (TimeoutException)
        {
            await WriteAuditAsync(
                database,
                sql,
                AuditResults.Rejected,
                "Business readonly query timed out.",
                rowCount: 0,
                isTruncated: false,
                durationMs: queryOptions.CommandTimeoutSeconds * 1000L,
                auditAction,
                cancellationToken);
            return Result.Invalid("Business readonly query timed out.");
        }
    }

    private static int ResolveLimit(BusinessDatabase database, int? requestedLimit)
    {
        var requested = requestedLimit.GetValueOrDefault(database.DefaultQueryLimit);
        if (requested <= 0)
        {
            requested = database.DefaultQueryLimit;
        }

        return Math.Clamp(requested, 1, database.MaxQueryLimit);
    }

    private async Task WriteAuditAsync(
        BusinessDatabase database,
        string sql,
        string result,
        string summary,
        int rowCount,
        bool isTruncated,
        long durationMs,
        string auditAction,
        CancellationToken cancellationToken)
    {
        var hash = BusinessQueryResultMapper.ComputeQueryHash(sql);
        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.DataAnalysis,
                auditAction,
                "BusinessDatabase",
                database.Id.ToString(),
                database.Name,
                result,
                summary,
                Metadata: new Dictionary<string, string>
                {
                    ["queryHash"] = hash,
                    ["sqlLength"] = (sql ?? string.Empty).Length.ToString(),
                    ["sourceMode"] = database.ExternalSystemType.ToString(),
                    ["rowCount"] = rowCount.ToString(),
                    ["isTruncated"] = isTruncated.ToString(),
                    ["durationMs"] = durationMs.ToString()
                }),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);
    }
}

public sealed record BusinessQuerySafetySchema(
    IReadOnlySet<string> AllowedTables,
    IReadOnlySet<string> BlockedFieldFragments);

internal static class BusinessReadonlyQuerySafetyPolicy
{
    private static readonly string[] DangerousSqlVerbs =
    [
        "insert",
        "update",
        "delete",
        "drop",
        "alter",
        "create",
        "truncate",
        "merge",
        "grant",
        "revoke",
        "copy",
        "execute",
        "call"
    ];

    private static readonly string[] SensitiveIdentifierFragments =
    [
        "api_key",
        "apikey",
        "connection_string",
        "credential",
        "password",
        "secret",
        "token"
    ];

    private static readonly string[] SystemCatalogFragments =
    [
        "information_schema.",
        "pg_catalog.",
        "pg_user",
        "pg_shadow",
        "sys.",
        "mysql."
    ];

    public static string? Validate(string sql, BusinessQuerySafetySchema? schema = null)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return "SQL statement cannot be empty.";
        }

        var normalized = StripComments(sql).Trim();
        var lower = normalized.ToLowerInvariant();
        if (!Regex.IsMatch(lower, @"^\s*select\b", RegexOptions.CultureInvariant))
        {
            return "Only SELECT statements are allowed.";
        }

        if (lower.Contains(';', StringComparison.Ordinal))
        {
            return "Multiple SQL statements are not allowed.";
        }

        if (DangerousSqlVerbs.Any(verb => Regex.IsMatch(lower, $@"\b{verb}\b", RegexOptions.CultureInvariant)))
        {
            return "DDL and DML statements are not allowed.";
        }

        if (SystemCatalogFragments.Any(fragment => lower.Contains(fragment, StringComparison.Ordinal)))
        {
            return "System catalog metadata is not allowed in business queries.";
        }

        var blockedFragments = SensitiveIdentifierFragments
            .Concat((IEnumerable<string>?)schema?.BlockedFieldFragments ?? Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (blockedFragments.Any(fragment => lower.Contains(fragment.ToLowerInvariant(), StringComparison.Ordinal)))
        {
            return "Sensitive fields such as passwords, tokens, keys, or connection strings are not allowed.";
        }

        if (schema is not null)
        {
            var tableNames = ExtractTableNames(lower).ToArray();
            if (tableNames.Length == 0)
            {
                return "Business query must reference an allowed business table.";
            }

            var blockedTable = tableNames.FirstOrDefault(table => !schema.AllowedTables.Contains(table));
            if (blockedTable is not null)
            {
                return $"Table '{blockedTable}' is not allowed for this data source.";
            }
        }

        return null;
    }

    private static IEnumerable<string> ExtractTableNames(string normalizedSql)
    {
        foreach (Match match in Regex.Matches(
                     normalizedSql,
                     @"\b(?:from|join)\s+([a-z_][a-z0-9_]*(?:\.[a-z_][a-z0-9_]*)?)",
                     RegexOptions.CultureInvariant))
        {
            var value = match.Groups[1].Value;
            var dot = value.LastIndexOf('.');
            yield return dot >= 0 ? value[(dot + 1)..] : value;
        }
    }

    private static string StripComments(string sql)
    {
        var withoutBlockComments = Regex.Replace(sql, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return Regex.Replace(withoutBlockComments, @"--.*?$", string.Empty, RegexOptions.Multiline);
    }
}

internal static class BusinessQueryResultMapper
{
    public const string SimulationSourceLabel = "AI \u72ec\u7acb\u6a21\u62df\u4e1a\u52a1\u5e93";

    public static BusinessQueryResultDto Map(
        BusinessDatabase database,
        string sql,
        DatabaseQueryResult queryResult)
    {
        var rows = queryResult.Rows
            .Select(row => (IReadOnlyDictionary<string, object?>)row)
            .ToArray();

        var columns = BuildColumns(queryResult.Rows);
        var sourceMode = BusinessDatabaseContractMapper.ToContractExternalSystemType(database.ExternalSystemType);
        var isSimulation = database.ExternalSystemType == BusinessDataExternalSystemType.SimulationBusiness;

        return new BusinessQueryResultDto(
            database.Id,
            database.Name,
            "BusinessDatabase",
            sourceMode,
            isSimulation,
            isSimulation ? SimulationSourceLabel : database.Name,
            ComputeQueryHash(sql),
            queryResult.ReturnedRowCount,
            queryResult.IsTruncated,
            columns,
            rows,
            DateTimeOffset.UtcNow,
            queryResult.ElapsedMilliseconds);
    }

    public static string ComputeQueryHash(string sql)
    {
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(sql ?? string.Empty)))
            .ToLowerInvariant();
    }

    private static IReadOnlyList<BusinessQueryColumnDto> BuildColumns(
        IReadOnlyList<Dictionary<string, object?>> rows)
    {
        var firstRow = rows.FirstOrDefault();
        if (firstRow is null)
        {
            return [];
        }

        return firstRow
            .Select(column => new BusinessQueryColumnDto(
                column.Key,
                column.Value?.GetType().Name ?? "Object"))
            .ToArray();
    }
}
