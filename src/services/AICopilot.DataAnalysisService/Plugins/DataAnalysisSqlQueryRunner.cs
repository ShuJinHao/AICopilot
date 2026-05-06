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
        var queryResult = await dbConnector.ExecuteQueryWithMetadataAsync(
            BusinessDatabaseContractMapper.ToConnectionInfo(db),
            sqlQuery,
            options: QueryOptions,
            cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var data = queryResult.Rows.ToList();
        var schema = BuildSchema(data.FirstOrDefault());

        var vizContext = serviceProvider.GetRequiredService<VisualizationContext>();
        vizContext.CaptureResult(data, schema);

        var auditLogWriter = serviceProvider.GetRequiredService<IAuditLogWriter>();
        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                "DataAnalysis",
                "DataAnalysis.ExecuteFreeSqlQuery",
                "BusinessDatabase",
                db.Id.ToString(),
                db.Name,
                AuditResults.Succeeded,
                $"自由 SQL 查询已执行。Rows={queryResult.ReturnedRowCount}; Truncated={queryResult.IsTruncated}; ElapsedMs={queryResult.ElapsedMilliseconds}."));
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        if (data.Count == 0)
        {
            return "查询执行成功，但未返回任何结果 (0 rows)。";
        }

        var preview = data.Take(5).ToJson();
        if (queryResult.IsTruncated)
        {
            return $"查询执行成功，结果已截断。共返回 {queryResult.ReturnedRowCount} 行，当前仅保留前 {data.Count} 行用于后续分析。预览数据：{preview}";
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
