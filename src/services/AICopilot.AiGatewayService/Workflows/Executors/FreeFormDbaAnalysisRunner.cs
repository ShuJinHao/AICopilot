using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Models;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Logging;

namespace AICopilot.AiGatewayService.Workflows.Executors;

public sealed class FreeFormDbaAnalysisRunner(
    DataAnalysisAgentBuilder agentBuilder,
    IBusinessDatabaseReadService businessDatabaseReadService,
    IDataAnalysisVisualizationContext vizContext,
    IAgentStreamRuntime chatStreamRuntime,
    DataAnalysisWidgetEmitter widgetEmitter,
    ILogger<FreeFormDbaAnalysisRunner> logger)
{
    public async Task<string> RunAsync(
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
                return $"[系统提示]: 无法连接数据库 {dbName}，请联系管理员核实配置。";
            }

            if (!db.IsReadOnly)
            {
                logger.LogWarning("意图指向数据库 '{DbName}'，但该库未配置为只读模式。", dbName);
                return $"[系统提示]: 数据库 {dbName} 未处于只读模式，系统已拒绝本次 AI 查询。";
            }

            await using var scopedAgent = await agentBuilder.BuildAsync(db);
            var thread = await scopedAgent.Agent.CreateSessionAsync(cancellationToken);
            var thinkTagFilter = new StreamingThinkTagFilter();

            await foreach (var update in scopedAgent.Agent.RunStreamingAsync(
                               intent.Query!,
                               thread,
                               cancellationToken: cancellationToken))
            {
                if (sink is null)
                {
                    continue;
                }

                await foreach (var chunk in chatStreamRuntime.CreateUpdateChunksAsync(
                                   update,
                                   DataAnalysisExecutor.ExecutorId,
                                   session,
                                   assistantText: null,
                                   appendAssistantText: false,
                                   cancellationToken,
                                   thinkTagFilter))
                {
                    await sink.WriteAsync(chunk, cancellationToken);
                }
            }

            var cleanRemainder = thinkTagFilter.Flush();
            if (!string.IsNullOrEmpty(cleanRemainder) && sink is not null)
            {
                await sink.WriteAsync(
                    new ChatChunk(DataAnalysisExecutor.ExecutorId, ChunkType.Text, cleanRemainder),
                    cancellationToken);
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
            logger.LogWarning(
                "执行数据分析意图时命中安全限制。Database: {DbName}; ErrorType={ErrorType}; OriginalMessage=hidden_by_security_policy",
                dbName,
                ex.GetType().Name);
            return $"[系统提示]: 查询数据库 {dbName} 的请求被系统安全策略拒绝。";
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
            return $"[系统提示]: 查询数据库 {dbName} 时发生异常，请稍后重试或联系管理员检查只读数据源配置。";
        }
    }
}
