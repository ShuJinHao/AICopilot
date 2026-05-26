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

[AuthorizeRequirement("DataSource.QueryGovernedSql")]
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
            auditAction: "DataSource.QueryGovernedSql",
            cancellationToken,
            selectionMode: DataSourceSelectionMode.GovernedSql);
    }
}

public sealed class BusinessReadonlyQueryExecutor(
    IReadRepository<BusinessDatabase> repository,
    IDatabaseConnector databaseConnector,
    BusinessDatabaseAccessService accessService,
    IAuditLogWriter auditLogWriter)
{
    public async Task<Result<BusinessQueryResultDto>> ExecuteAsync(
        Guid dataSourceId,
        string sql,
        int? limit,
        bool requireSimulationBusiness,
        BusinessQuerySafetySchema? safetySchema,
        string auditAction,
        CancellationToken cancellationToken,
        DataSourceSelectionMode selectionMode = DataSourceSelectionMode.Query)
    {
        var database = await repository.FirstOrDefaultAsync(
            new BusinessDatabaseByIdSpec(new BusinessDatabaseId(dataSourceId)),
            cancellationToken);
        if (database is null)
        {
            return Result.NotFound();
        }

        if (!await accessService.CanQueryAsync(database, cancellationToken))
        {
            await WriteAuditAsync(
                database,
                sql,
                AuditResults.Rejected,
                "Business readonly query rejected because the current user is not authorized for this data source.",
                rowCount: 0,
                isTruncated: false,
                durationMs: 0,
                selectionMode,
                warningCode: "DATA_SOURCE_FORBIDDEN",
                auditAction,
                cancellationToken);
            return Result.Forbidden(new ApiProblemDescriptor(
                "data_source_forbidden",
                "Current user is not authorized to query this business data source."));
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
                selectionMode,
                warningCode: "SOURCE_DISABLED_OR_NOT_READONLY",
                auditAction,
                cancellationToken);
            return Result.Invalid("Business database is disabled or not readonly.");
        }

        var credentialError = BusinessDataSourceGovernancePolicy.ValidateReadOnlyCredential(database);
        if (credentialError is not null)
        {
            await WriteAuditAsync(
                database,
                sql,
                AuditResults.Rejected,
                credentialError,
                rowCount: 0,
                isTruncated: false,
                durationMs: 0,
                selectionMode,
                warningCode: "READONLY_CREDENTIAL_UNVERIFIED",
                auditAction,
                cancellationToken);
            return Result.Invalid(credentialError);
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
                selectionMode,
                warningCode: "SIMULATION_BUSINESS_REQUIRED",
                auditAction,
                cancellationToken);
            return Result.Invalid("P1 Text-to-SQL only executes SimulationBusiness data sources.");
        }

        var effectiveSafetySchema = safetySchema ?? BusinessDataSourceGovernancePolicy.ResolveSafetySchema(database);
        if (effectiveSafetySchema is null)
        {
            const string schemaError = "Governed semantic schema is required before executing this business data source.";
            await WriteAuditAsync(
                database,
                sql,
                AuditResults.Rejected,
                schemaError,
                rowCount: 0,
                isTruncated: false,
                durationMs: 0,
                selectionMode,
                warningCode: "GOVERNED_SCHEMA_REQUIRED",
                auditAction,
                cancellationToken);
            return Result.Invalid(schemaError);
        }

        var safetyError = BusinessReadonlyQuerySafetyPolicy.Validate(sql, effectiveSafetySchema);
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
                selectionMode,
                warningCode: BusinessDataSourceGovernancePolicy.ResolveWarningCode(safetyError),
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

            var dto = BusinessQueryResultMapper.Map(database, sql, queryResult, effectiveSafetySchema, selectionMode);
            await WriteAuditAsync(
                database,
                sql,
                AuditResults.Succeeded,
                $"Business readonly query executed. RowsObserved={queryResult.ReturnedRowCount}; Truncated={queryResult.IsTruncated}; DurationMs={queryResult.ElapsedMilliseconds}.",
                queryResult.ReturnedRowCount,
                queryResult.IsTruncated,
                queryResult.ElapsedMilliseconds,
                selectionMode,
                warningCode: string.Join(",", dto.Governance?.WarningCodes ?? []),
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
                selectionMode,
                warningCode: "SQL_GUARDRAIL_REJECTED",
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
                selectionMode,
                warningCode: "QUERY_TIMEOUT",
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
        DataSourceSelectionMode selectionMode,
        string warningCode,
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
                    ["dataSourceId"] = database.Id.ToString(),
                    ["selectionMode"] = selectionMode.ToString(),
                    ["rowCount"] = rowCount.ToString(),
                    ["isTruncated"] = isTruncated.ToString(),
                    ["durationMs"] = durationMs.ToString(),
                    ["warningCode"] = string.IsNullOrWhiteSpace(warningCode) ? "NONE" : warningCode
                }),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);
    }
}

public sealed record BusinessQuerySafetySchema(
    IReadOnlySet<string> AllowedTables,
    IReadOnlySet<string> BlockedFieldFragments,
    IReadOnlySet<string>? AllowedColumnFragments = null,
    IReadOnlySet<string>? SensitiveColumnFragments = null);

internal static class BusinessDataSourceGovernancePolicy
{
    public static bool IsSelectableForMode(
        BusinessDatabase database,
        DataSourceSelectionMode selectionMode)
    {
        if (!database.IsEnabled || !database.IsReadOnly)
        {
            return false;
        }

        return selectionMode switch
        {
            DataSourceSelectionMode.Agent => database.IsSelectableInAgent &&
                                             database.ExternalSystemType == BusinessDataExternalSystemType.SimulationBusiness,
            DataSourceSelectionMode.Chat => database.IsSelectableInChat,
            DataSourceSelectionMode.TextToSql => database.IsSelectableInChat &&
                                                 database.ExternalSystemType == BusinessDataExternalSystemType.SimulationBusiness,
            DataSourceSelectionMode.GovernedSql => true,
            DataSourceSelectionMode.Query => true,
            _ => false
        };
    }

    public static bool HasExecutableGovernedSchema(BusinessDatabase database)
    {
        return database.IsEnabled &&
               database.IsReadOnly &&
               ResolveSafetySchema(database) is not null &&
               ValidateReadOnlyCredential(database) is null;
    }

    public static string ResolveGovernanceStatus(BusinessDatabase database)
    {
        if (!database.IsEnabled)
        {
            return "Disabled";
        }

        if (!database.IsReadOnly)
        {
            return "BlockedNotReadOnly";
        }

        var credentialError = ValidateReadOnlyCredential(database);
        if (credentialError is not null)
        {
            return "BlockedUnverifiedReadOnlyCredential";
        }

        return ResolveSafetySchema(database) is null
            ? "BlockedUntilGovernedSchema"
            : "GovernedSchemaReady";
    }

    public static BusinessQuerySafetySchema? ResolveSafetySchema(BusinessDatabase database)
    {
        return database.ExternalSystemType == BusinessDataExternalSystemType.SimulationBusiness
            ? SimulationBusinessQuerySchema.SafetySchema
            : null;
    }

    public static string? ValidateReadOnlyCredential(BusinessDatabase database)
    {
        if (!database.IsEnabled)
        {
            return null;
        }

        if (database.ExternalSystemType == BusinessDataExternalSystemType.CloudReadOnly &&
            !database.ReadOnlyCredentialVerified)
        {
            return "Cloud read-only data source requires a verified readonly credential before execution.";
        }

        if (database.Provider != DbProviderType.PostgreSql && !database.ReadOnlyCredentialVerified)
        {
            return "SQL Server/MySQL data source requires a verified readonly credential before execution.";
        }

        return null;
    }

    public static string ResolveWarningCode(string safetyError)
    {
        if (safetyError.Contains("Wildcard", StringComparison.OrdinalIgnoreCase))
        {
            return "WILDCARD_PROJECTION_REJECTED";
        }

        if (safetyError.Contains("Table", StringComparison.OrdinalIgnoreCase))
        {
            return "TABLE_NOT_ALLOWLISTED";
        }

        if (safetyError.Contains("Sensitive", StringComparison.OrdinalIgnoreCase))
        {
            return "SENSITIVE_FIELD_REJECTED";
        }

        if (safetyError.Contains("DDL", StringComparison.OrdinalIgnoreCase) ||
            safetyError.Contains("DML", StringComparison.OrdinalIgnoreCase))
        {
            return "WRITE_SQL_REJECTED";
        }

        if (safetyError.Contains("Multiple", StringComparison.OrdinalIgnoreCase))
        {
            return "MULTI_STATEMENT_REJECTED";
        }

        if (safetyError.Contains("System catalog", StringComparison.OrdinalIgnoreCase))
        {
            return "SYSTEM_CATALOG_REJECTED";
        }

        return "SQL_GOVERNANCE_REJECTED";
    }
}

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

        var blockedFragments = AllSensitiveFragments(schema);
        if (blockedFragments.Any(fragment => lower.Contains(fragment.ToLowerInvariant(), StringComparison.Ordinal)))
        {
            return "Sensitive fields such as passwords, tokens, keys, or connection strings are not allowed.";
        }

        if (schema is not null)
        {
            if (ContainsWildcardProjection(lower))
            {
                return "Wildcard SELECT projections are not allowed in governed business queries.";
            }

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
        else
        {
            return "Governed query safety schema is required.";
        }

        return null;
    }

    public static IReadOnlyList<string> AllSensitiveFragments(BusinessQuerySafetySchema? schema)
    {
        return SensitiveIdentifierFragments
            .Concat((IEnumerable<string>?)schema?.BlockedFieldFragments ?? Array.Empty<string>())
            .Concat((IEnumerable<string>?)schema?.SensitiveColumnFragments ?? Array.Empty<string>())
            .Where(fragment => !string.IsNullOrWhiteSpace(fragment))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool ContainsWildcardProjection(string normalizedSql)
    {
        var selectMatch = Regex.Match(
            normalizedSql,
            @"^\s*select\s+(?:all\s+|distinct\s+)?(?<projection>.*?)\bfrom\b",
            RegexOptions.CultureInvariant | RegexOptions.Singleline);
        if (!selectMatch.Success)
        {
            return false;
        }

        var projection = selectMatch.Groups["projection"].Value;
        return Regex.IsMatch(
            projection,
            @"(^|,)\s*(?:[a-z_][a-z0-9_]*\.)?\*\s*(,|$)",
            RegexOptions.CultureInvariant);
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
    private const int MaxPreviewRows = 50;
    private const int MaxStringValueLength = 512;

    public static BusinessQueryResultDto Map(
        BusinessDatabase database,
        string sql,
        DatabaseQueryResult queryResult,
        BusinessQuerySafetySchema safetySchema,
        DataSourceSelectionMode selectionMode)
    {
        var sanitization = SanitizeRows(queryResult.Rows, safetySchema);
        var rows = sanitization.Rows;

        var columns = BuildColumns(rows);
        var sourceMode = BusinessDatabaseContractMapper.ToContractExternalSystemType(database.ExternalSystemType);
        var isSimulation = database.ExternalSystemType == BusinessDataExternalSystemType.SimulationBusiness;
        var warningCodes = new List<string> { "SANITIZED_PREVIEW" };
        if (queryResult.Rows.Count > rows.Count)
        {
            warningCodes.Add("BOUNDED_PREVIEW_APPLIED");
        }

        if (sanitization.RedactedColumnHashes.Count > 0 || sanitization.RedactedValueCount > 0)
        {
            warningCodes.Add("SENSITIVE_VALUE_REDACTED");
        }

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
            queryResult.ElapsedMilliseconds,
            new BusinessQueryGovernanceDto(
                IsSanitizedPreview: true,
                selectionMode,
                rows.Count,
                MaxPreviewRows,
                warningCodes,
                sanitization.RedactedColumnHashes,
                safetySchema.AllowedTables.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray()));
    }

    public static string ComputeQueryHash(string sql)
    {
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(sql ?? string.Empty)))
            .ToLowerInvariant();
    }

    private static SanitizedRows SanitizeRows(
        IReadOnlyList<Dictionary<string, object?>> rows,
        BusinessQuerySafetySchema safetySchema)
    {
        var blockedFragments = BusinessReadonlyQuerySafetyPolicy.AllSensitiveFragments(safetySchema);
        var redactedColumnHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var redactedValueCount = 0;
        var columnMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sanitizedRows = rows
            .Take(MaxPreviewRows)
            .Select(row =>
            {
                var sanitized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                var columnIndex = 0;
                foreach (var item in row)
                {
                    var safeName = ResolveSafeColumnName(item.Key, columnIndex, blockedFragments, redactedColumnHashes, columnMap);
                    var value = SanitizeValue(item.Key, item.Value, blockedFragments, ref redactedValueCount);
                    sanitized[safeName] = value;
                    columnIndex++;
                }

                return (IReadOnlyDictionary<string, object?>)sanitized;
            })
            .ToArray();

        return new SanitizedRows(
            sanitizedRows,
            redactedColumnHashes.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray(),
            redactedValueCount);
    }

    private static string ResolveSafeColumnName(
        string columnName,
        int columnIndex,
        IReadOnlyCollection<string> blockedFragments,
        ISet<string> redactedColumnHashes,
        IDictionary<string, string> columnMap)
    {
        if (columnMap.TryGetValue(columnName, out var mapped))
        {
            return mapped;
        }

        if (!ContainsSensitiveFragment(columnName, blockedFragments))
        {
            columnMap[columnName] = columnName;
            return columnName;
        }

        redactedColumnHashes.Add(ComputeQueryHash(columnName));
        var safeName = $"redacted_column_{columnIndex + 1}";
        columnMap[columnName] = safeName;
        return safeName;
    }

    private static object? SanitizeValue(
        string columnName,
        object? value,
        IReadOnlyCollection<string> blockedFragments,
        ref int redactedValueCount)
    {
        if (value is null)
        {
            return null;
        }

        if (ContainsSensitiveFragment(columnName, blockedFragments))
        {
            redactedValueCount++;
            return "[redacted]";
        }

        if (value is string text)
        {
            if (ContainsSensitiveFragment(text, blockedFragments) ||
                Regex.IsMatch(text, @"(?i)(sk-[a-z0-9_\-]{8,}|password\s*=|token\s*=|api[_-]?key\s*=|connection\s*string|secret\s*=)"))
            {
                redactedValueCount++;
                return "[redacted]";
            }

            return text.Length <= MaxStringValueLength
                ? text
                : string.Concat(text.AsSpan(0, MaxStringValueLength), "...[truncated]");
        }

        var valueType = value.GetType();
        if (valueType.IsPrimitive ||
            value is decimal ||
            value is DateTime ||
            value is DateTimeOffset ||
            value is Guid)
        {
            return value;
        }

        redactedValueCount++;
        return "[redacted]";
    }

    private static bool ContainsSensitiveFragment(
        string value,
        IReadOnlyCollection<string> blockedFragments)
    {
        return blockedFragments.Any(fragment =>
            !string.IsNullOrWhiteSpace(fragment) &&
            value.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<BusinessQueryColumnDto> BuildColumns(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
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

    private sealed record SanitizedRows(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
        IReadOnlyList<string> RedactedColumnHashes,
        int RedactedValueCount);
}
