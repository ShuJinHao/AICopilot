using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.DataAnalysisService.BusinessDatabases;
using AICopilot.DataAnalysisService.Services;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Serialization;
using AICopilot.Visualization;
using Microsoft.Extensions.DependencyInjection;

namespace AICopilot.DataAnalysisService.Plugins;

internal static class DataAnalysisSqlQueryRunner
{
    private static readonly DatabaseQueryOptions QueryOptions = new(MaxRows: 200, CommandTimeoutSeconds: 15);

    public static async Task<string> ExecuteAsync(
        IServiceProvider serviceProvider,
        IDatabaseConnector dbConnector,
        BusinessDatabase db,
        string sqlQuery,
        CancellationToken cancellationToken)
    {
        var safetySchema = BusinessDataSourceGovernancePolicy.ResolveSafetySchema(db);
        if (safetySchema is null)
        {
            throw new InvalidOperationException("Governed semantic schema is required before executing this business data source.");
        }

        var safetyError = BusinessReadonlyQuerySafetyPolicy.Validate(sqlQuery, safetySchema);
        if (safetyError is not null)
        {
            throw new InvalidOperationException(safetyError);
        }

        var queryResult = await dbConnector.ExecuteQueryWithMetadataAsync(
            BusinessDatabaseContractMapper.ToConnectionInfo(db),
            sqlQuery,
            options: QueryOptions,
            cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var dto = BusinessQueryResultMapper.Map(
            db,
            sqlQuery,
            queryResult,
            safetySchema,
            DataSourceSelectionMode.Agent);
        var data = dto.Rows.ToList();
        var schema = BuildSchema(data.FirstOrDefault());

        var vizContext = serviceProvider.GetRequiredService<VisualizationContext>();
        vizContext.CaptureResult(data, schema);

        var auditLogWriter = serviceProvider.GetRequiredService<IAuditLogWriter>();
        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.DataAnalysis,
                "DataAnalysis.ExecuteGovernedSqlQuery",
                "BusinessDatabase",
                db.Id.ToString(),
                db.Name,
                AuditResults.Succeeded,
                $"Governed SQL query executed. RowsObserved={queryResult.ReturnedRowCount}; Truncated={queryResult.IsTruncated}; ElapsedMs={queryResult.ElapsedMilliseconds}.",
                Metadata: new Dictionary<string, string>
                {
                    ["queryHash"] = BusinessQueryResultMapper.ComputeQueryHash(sqlQuery),
                    ["sqlLength"] = sqlQuery.Length.ToString(),
                    ["sourceMode"] = db.ExternalSystemType.ToString(),
                    ["dataSourceId"] = db.Id.ToString(),
                    ["selectionMode"] = DataSourceSelectionMode.Agent.ToString(),
                    ["rowCount"] = queryResult.ReturnedRowCount.ToString(),
                    ["isTruncated"] = queryResult.IsTruncated.ToString(),
                    ["durationMs"] = queryResult.ElapsedMilliseconds.ToString(),
                    ["warningCode"] = string.Join(",", dto.Governance?.WarningCodes ?? [])
                }));
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        if (data.Count == 0)
        {
            return "查询执行成功，但未返回任何结果 (0 rows)。";
        }

        var preview = data.Take(5).ToJson();
        if (queryResult.IsTruncated)
        {
            return $"查询执行成功，结果已截断。已检测到至少 {queryResult.ReturnedRowCount} 行，当前仅保留前 {data.Count} 行用于后续分析。预览数据：{preview}";
        }

        return preview;
    }

    private static List<SchemaColumn> BuildSchema(IReadOnlyDictionary<string, object?>? firstRow)
    {
        if (firstRow is null)
        {
            return [];
        }

        return firstRow
            .Select(item => new SchemaColumn(item.Key, item.Value?.GetType() ?? typeof(object)))
            .ToList();
    }
}
