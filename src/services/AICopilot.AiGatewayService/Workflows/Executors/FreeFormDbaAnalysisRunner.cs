using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Approvals;
using AICopilot.AiGatewayService.Models;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Logging;

namespace AICopilot.AiGatewayService.Workflows.Executors;

public sealed class FreeFormDbaAnalysisRunner(
    DataAnalysisAgentBuilder agentBuilder,
    IBusinessDatabaseReadService businessDatabaseReadService,
    IDataAnalysisVisualizationContext vizContext,
    ApprovalRequirementResolver approvalRequirementResolver,
    DataAnalysisWidgetEmitter widgetEmitter,
    ILogger<FreeFormDbaAnalysisRunner> logger)
{
    public async Task<string> RunAsync(
        IntentResult intent,
        ChatWorkflowSink? sink,
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
                return $"[系统提示]: 无法连接数据库 {dbName}，请联系管理员核实配置。";
            }

            if (!db.IsReadOnly)
            {
                logger.LogWarning("意图指向数据库 '{DbName}'，但该库未配置为只读模式。", dbName);
                return $"[系统提示]: 数据库 {dbName} 未处于只读模式，系统已拒绝本次 AI 查询。";
            }

            await using var scopedAgent = await agentBuilder.BuildAsync(db);
            var thread = await scopedAgent.Agent.CreateSessionAsync(cancellationToken);

            await foreach (var update in scopedAgent.Agent.RunStreamingAsync(
                               intent.Query!,
                               thread,
                               cancellationToken: cancellationToken))
            {
                if (sink is null)
                {
                    continue;
                }

                await foreach (var chunk in ChatStreamRuntime.CreateUpdateChunksAsync(
                                   approvalRequirementResolver,
                                   update,
                                   DataAnalysisExecutor.ExecutorId,
                                   session,
                                   assistantText: null,
                                   appendAssistantText: false,
                                   cancellationToken))
                {
                    await sink.WriteAsync(chunk, cancellationToken);
                }
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

            return DataAnalysisFinalContextFormatter.FormatFreeForm(
                output.Analysis,
                output.Decision,
                rawData,
                schema);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "执行数据分析意图时命中安全限制。Database: {DbName}", dbName);
            return $"[系统提示]: 查询数据库 {dbName} 的请求被系统安全策略拒绝。";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "执行数据分析意图失败。Database: {DbName}", dbName);
            return $"[系统提示]: 查询数据库 {dbName} 时发生异常，请稍后重试或联系管理员检查只读数据源配置。";
        }
    }
}
