using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.Workflows.Executors;

public sealed class DataAnalysisAuditRecorder(IAuditLogWriter auditLogWriter)
{
    public async Task RecordSemanticQueryAsync(
        BusinessDatabaseConnectionInfo businessDatabase,
        SemanticQueryPlan plan,
        SemanticPhysicalMapping mapping,
        DatabaseQueryResult queryResult,
        CancellationToken cancellationToken)
    {
        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                "DataAnalysis",
                "DataAnalysis.ExecuteSemanticQuery",
                "BusinessDatabase",
                businessDatabase.Id.ToString(),
                businessDatabase.Name,
                AuditResults.Succeeded,
                $"语义查询已执行。Target={plan.Target}; Source={mapping.SourceName}; RowsObserved={queryResult.ReturnedRowCount}; Truncated={queryResult.IsTruncated}; ElapsedMs={queryResult.ElapsedMilliseconds}."),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordBusinessTextToSqlFallbackAsync(
        BusinessDatabaseConnectionInfo businessDatabase,
        string result,
        string summary,
        string questionHash,
        string sqlHash,
        int rowCount,
        bool isTruncated,
        IReadOnlyList<CloudReadOnlyTextToSqlRepairAttemptRecord> repairAttempts,
        CancellationToken cancellationToken)
    {
        var lastAttempt = repairAttempts.LastOrDefault();
        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                "DataAnalysis",
                "DataAnalysis.BusinessTextToSqlFallback",
                "BusinessDatabase",
                businessDatabase.Id.ToString(),
                businessDatabase.Name,
                result,
                summary,
                Metadata: new Dictionary<string, string>
                {
                    ["questionHash"] = questionHash,
                    ["sqlHash"] = sqlHash,
                    ["sourceMode"] = businessDatabase.ExternalSystemType.ToString(),
                    ["rowCount"] = rowCount.ToString(),
                    ["isTruncated"] = isTruncated.ToString(),
                    ["repairAttemptCount"] = repairAttempts.Count.ToString(),
                    ["lastFailureCode"] = lastAttempt?.FailureCode.ToString() ?? string.Empty,
                    ["lastFailureStage"] = lastAttempt?.Stage.ToString() ?? string.Empty
                }),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);
    }
}
