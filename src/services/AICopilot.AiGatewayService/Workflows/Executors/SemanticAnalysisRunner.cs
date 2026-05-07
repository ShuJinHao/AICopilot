using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Workflows;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Logging;

namespace AICopilot.AiGatewayService.Workflows.Executors;

public sealed class SemanticAnalysisRunner(
    IBusinessDatabaseReadService businessDatabaseReadService,
    IDatabaseConnector databaseConnector,
    ISemanticQueryPlanner semanticQueryPlanner,
    ISemanticPhysicalMappingProvider semanticPhysicalMappingProvider,
    ISemanticSqlGenerator semanticSqlGenerator,
    DataAnalysisAuditRecorder auditRecorder,
    ILogger<SemanticAnalysisRunner> logger)
{
    private static readonly DatabaseQueryOptions QueryOptions = new(MaxRows: 200, CommandTimeoutSeconds: 15);

    public async Task<string> RunAsync(IntentResult intent, CancellationToken cancellationToken)
    {
        var planningResult = semanticQueryPlanner.Plan(intent.Intent, intent.Query);
        if (!planningResult.IsSuccess)
        {
            var failedTargetLabel = SemanticAnalysisPresentation.TryGetTargetLabel(intent.Intent);
            logger.LogWarning(
                "{TargetLabel}语义查询规划失败。Intent: {Intent}, Error: {Error}",
                failedTargetLabel,
                intent.Intent,
                planningResult.ErrorMessage);
            return $"[系统提示]: {failedTargetLabel}语义查询规划失败 - {planningResult.ErrorMessage}";
        }

        var plan = planningResult.Plan!;
        var targetLabel = SemanticAnalysisPresentation.GetTargetLabel(plan.Target);
        if (!semanticPhysicalMappingProvider.TryGetMapping(plan.Target, out var mapping))
        {
            logger.LogInformation(
                "{TargetLabel}语义查询已识别，但尚未绑定只读业务库映射。Intent: {Intent}, Target: {Target}, Kind: {Kind}",
                targetLabel,
                plan.Intent,
                plan.Target,
                plan.Kind);
            return $"[系统提示]: 当前未找到{targetLabel}语义映射，请联系管理员检查后端映射配置。";
        }

        if (string.IsNullOrWhiteSpace(mapping.DatabaseName))
        {
            logger.LogWarning(
                "{TargetLabel}语义映射缺少目标数据库名称。Intent: {Intent}, Target: {Target}",
                targetLabel,
                plan.Intent,
                plan.Target);
            return $"[系统提示]: 当前{targetLabel}语义映射未绑定只读业务库，请联系管理员检查映射配置。";
        }

        var businessDatabase = await businessDatabaseReadService.GetByNameAsync(
            mapping.DatabaseName,
            cancellationToken);

        if (businessDatabase == null || !businessDatabase.IsEnabled)
        {
            logger.LogWarning(
                "{TargetLabel}语义查询找不到启用中的只读业务库。Intent: {Intent}, DatabaseName: {DatabaseName}",
                targetLabel,
                plan.Intent,
                mapping.DatabaseName);
            return $"[系统提示]: 当前未找到可用的{targetLabel}只读数据源，请联系管理员检查配置。";
        }

        if (!businessDatabase.IsReadOnly)
        {
            logger.LogWarning(
                "{TargetLabel}语义查询命中的业务库未处于只读模式。Intent: {Intent}, DatabaseName: {DatabaseName}",
                targetLabel,
                plan.Intent,
                businessDatabase.Name);
            return $"[系统提示]: 当前{targetLabel}数据源未处于只读模式，系统已拒绝本次查询。";
        }

        if (businessDatabase.Provider != mapping.Provider)
        {
            logger.LogWarning(
                "{TargetLabel}语义查询的业务库类型与映射定义不匹配。Intent: {Intent}, DatabaseName: {DatabaseName}, DatabaseProvider: {DatabaseProvider}, MappingProvider: {MappingProvider}",
                targetLabel,
                plan.Intent,
                businessDatabase.Name,
                businessDatabase.Provider,
                mapping.Provider);
            return $"[系统提示]: 当前{targetLabel}数据源类型与语义映射不匹配，请联系管理员检查配置。";
        }

        GeneratedSemanticSql generatedSql;
        try
        {
            generatedSql = semanticSqlGenerator.Generate(plan, mapping);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "{TargetLabel}语义查询未通过白名单 SQL 生成。Intent: {Intent}, DatabaseName: {DatabaseName}",
                targetLabel,
                plan.Intent,
                businessDatabase.Name);
            return $"[系统提示]: 当前{targetLabel}查询请求未通过安全白名单校验，系统已拒绝执行。";
        }

        try
        {
            var queryResult = await databaseConnector.ExecuteQueryWithMetadataAsync(
                businessDatabase,
                generatedSql.SqlText,
                generatedSql.Parameters,
                QueryOptions,
                cancellationToken);

            var normalizedRows = queryResult.Rows.ToList();
            var semanticSummary = SemanticSummaryBuilder.Build(plan, normalizedRows);
            await auditRecorder.RecordSemanticQueryAsync(
                businessDatabase,
                plan,
                mapping,
                queryResult,
                cancellationToken);
            logger.LogInformation(
                "{TargetLabel}语义查询执行完成。Intent: {Intent}, Target: {Target}, Kind: {Kind}, DatabaseName: {DatabaseName}, Source: {Source}, RowCount: {RowCount}, Truncated: {Truncated}",
                targetLabel,
                plan.Intent,
                plan.Target,
                plan.Kind,
                businessDatabase.Name,
                mapping.SourceName,
                queryResult.ReturnedRowCount,
                queryResult.IsTruncated);

            var sourceLabel = SemanticAnalysisPresentation.BuildBusinessSourceLabel(
                targetLabel,
                businessDatabase.ExternalSystemType);
            var analysis = SemanticAnalysisPresentation.BuildAnalysis(
                plan,
                sourceLabel,
                semanticSummary,
                queryResult.IsTruncated);
            return DataAnalysisFinalContextFormatter.FormatSemantic(
                analysis,
                semanticSummary,
                normalizedRows,
                queryResult.IsTruncated);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(
                ex,
                "{TargetLabel}语义查询在执行阶段被安全规则拒绝。Intent: {Intent}, DatabaseName: {DatabaseName}",
                targetLabel,
                plan.Intent,
                businessDatabase.Name);
            return $"[系统提示]: 当前{targetLabel}查询请求被系统安全策略拒绝。";
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "{TargetLabel}语义查询执行失败。Intent: {Intent}, DatabaseName: {DatabaseName}",
                targetLabel,
                plan.Intent,
                businessDatabase.Name);
            return $"[系统提示]: 当前{targetLabel}数据源暂时不可用，请稍后重试或联系管理员检查连接。";
        }
    }
}
