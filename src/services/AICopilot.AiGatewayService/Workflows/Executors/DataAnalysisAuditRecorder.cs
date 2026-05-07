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
                $"语义查询已执行。Target={plan.Target}; Source={mapping.SourceName}; Rows={queryResult.ReturnedRowCount}; Truncated={queryResult.IsTruncated}; ElapsedMs={queryResult.ElapsedMilliseconds}."),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);
    }
}
