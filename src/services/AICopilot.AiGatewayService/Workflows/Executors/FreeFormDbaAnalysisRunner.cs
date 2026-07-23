using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Models;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Logging;

namespace AICopilot.AiGatewayService.Workflows.Executors;

public sealed class FreeFormDbaAnalysisRunner(
    DataAnalysisAgentBuilder agentBuilder,
    IBusinessDatabaseReadService businessDatabaseReadService,
    IDataAnalysisVisualizationContext vizContext,
    DataAnalysisWidgetEmitter widgetEmitter,
    ILogger<FreeFormDbaAnalysisRunner> logger)
{
    internal async Task<AgentAnalysisNodeResult> RunAsync(
        IntentResult intent,
        AgentWorkflowSink? sink,
        SessionRuntimeSnapshot? session,
        CancellationToken cancellationToken)
    {
        var dbName = intent.Intent[DataAnalysisExecutor.AnalysisIntentPrefix.Length..];

        try
        {
            var db = await businessDatabaseReadService.GetByNameAsync(dbName, cancellationToken);

            if (db == null || !db.IsEnabled)
            {
                logger.LogWarning("意图指向数据库 '{DbName}'，但该库不存在或已禁用。", dbName);
                return AgentAnalysisNodeResult.Failed(
                    AppProblemCodes.ChatConfigurationMissing,
                    $"无法连接只读数据源 {dbName}，请联系管理员核实配置。");
            }

            if (!db.IsReadOnly)
            {
                logger.LogWarning("意图指向数据库 '{DbName}'，但该库未配置为只读模式。", dbName);
                return AgentAnalysisNodeResult.Failed(
                    AppProblemCodes.AgentPlanToolDenied,
                    $"数据源 {dbName} 未处于只读模式，系统已拒绝本次 AI 查询。");
            }

            await using var scopedAgent = await agentBuilder.BuildAsync(db);
            var thread = await scopedAgent.Agent.CreateSessionAsync(cancellationToken);
            await foreach (var _ in scopedAgent.Agent.RunStreamingAsync(
                               intent.Query!,
                               thread,
                               cancellationToken: cancellationToken))
            {
                // Child-agent conversation is intentionally not streamed into the
                // parent transcript. Only normalized Evidence and typed widgets cross
                // the branch boundary.
            }

            logger.LogInformation("数据库 {DbName} 查询完成。", dbName);

            var (rawData, schema) = vizContext.GetLastResult();
            var output = vizContext.GetOutput();

            if (output is { Decision: not null } && vizContext.HasData)
            {
                await widgetEmitter.TryEmitAsync(
                    output,
                    rawData,
                    schema,
                    sink,
                    dbName,
                    cancellationToken);
            }

            var safeContext = DataAnalysisFinalContextFormatter.FormatFreeForm(
                output.Analysis,
                output.Decision,
                rawData,
                schema);
            var evidence = AgentBranchEvidenceSeed.CreateObservedDataQuery(
                "GovernedDataReadNode",
                safeContext,
                "governed-text-to-sql:v1",
                "GovernedTextToSql",
                "GovernedReadOnly",
                isSimulation: null,
                intent.Intent,
                ["governed-readonly"]);
            return vizContext.HasData
                ? AgentAnalysisNodeResult.Succeeded(evidence)
                : AgentAnalysisNodeResult.Empty(evidence);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(
                "执行数据分析意图时命中安全限制。Database: {DbName}; ErrorType={ErrorType}; OriginalMessage=hidden_by_security_policy",
                dbName,
                ex.GetType().Name);
            return AgentAnalysisNodeResult.Failed(
                AppProblemCodes.AgentPlanToolDenied,
                $"查询数据源 {dbName} 的请求被系统安全策略拒绝。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                "执行数据分析意图失败。Database: {DbName}; ErrorType={ErrorType}; OriginalMessage=hidden_by_security_policy",
                dbName,
                ex.GetType().Name);
            return AgentAnalysisNodeResult.Failed(
                AppProblemCodes.ChatStreamFailed,
                $"查询数据源 {dbName} 时发生异常，请稍后重试或联系管理员检查只读数据源配置。");
        }
    }
}
