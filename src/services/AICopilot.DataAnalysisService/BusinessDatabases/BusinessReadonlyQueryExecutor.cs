using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Core.DataAnalysis.Ids;
using AICopilot.Core.DataAnalysis.Specifications.BusinessDatabase;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.DataAnalysisService.BusinessDatabases;

public sealed class BusinessReadonlyQueryExecutor(
    IReadRepository<BusinessDatabase> repository,
    IDatabaseConnector databaseConnector,
    BusinessDatabaseAccessService accessService,
    IAuditLogWriter auditLogWriter)
{
    private readonly BusinessReadonlyQueryAuditRecorder auditRecorder = new(auditLogWriter);

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
            await auditRecorder.WriteAsync(
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
            await auditRecorder.WriteAsync(
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
            await auditRecorder.WriteAsync(
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
            await auditRecorder.WriteAsync(
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
            await auditRecorder.WriteAsync(
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
            await auditRecorder.WriteAsync(
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
            await auditRecorder.WriteAsync(
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
            await auditRecorder.WriteAsync(
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
            await auditRecorder.WriteAsync(
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
}
