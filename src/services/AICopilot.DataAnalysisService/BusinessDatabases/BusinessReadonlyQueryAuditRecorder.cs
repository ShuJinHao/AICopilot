using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Services.Contracts;

namespace AICopilot.DataAnalysisService.BusinessDatabases;

internal sealed class BusinessReadonlyQueryAuditRecorder(IAuditLogWriter auditLogWriter)
{
    public async Task WriteAsync(
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
