using AICopilot.AiGatewayService.Agents;
using System.Text;
using AICopilot.AiGatewayService.Models;
using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.AiGateway.Dtos;
using Microsoft.Extensions.Logging;

namespace AICopilot.AiGatewayService.Workflows.Executors;

/// <summary>
/// 数据分析执行器只负责 Analysis.* 意图分派，具体执行由 semantic/free-form runner 承担。
/// </summary>
public class DataAnalysisExecutor(
    ISemanticIntentCatalog semanticIntentCatalog,
    SemanticAnalysisRunner semanticRunner,
    FreeFormDbaAnalysisRunner freeFormRunner,
    ILogger<DataAnalysisExecutor> logger)
{
    public const string ExecutorId = nameof(DataAnalysisExecutor);
    public const string AnalysisIntentPrefix = "Analysis.";

    public async Task<BranchResult> ExecuteAsync(
        List<IntentResult> intentResults,
        ChatWorkflowSink? sink,
        SessionRuntimeSnapshot? session,
        CancellationToken ct = default)
    {
        var analysisIntents = intentResults
            .Where(i => i.Intent.StartsWith(AnalysisIntentPrefix, StringComparison.OrdinalIgnoreCase)
                        && i.Confidence > 0.6)
            .ToList();

        if (analysisIntents.Count == 0)
        {
            logger.LogDebug("未检测到数据分析意图，跳过执行。");
            return BranchResult.FromDataAnalysis(string.Empty);
        }

        logger.LogInformation("启动数据分析流程，命中 Analysis.* 意图数量: {Count}", analysisIntents.Count);

        var semanticIntents = analysisIntents
            .Where(intent => semanticIntentCatalog.TryGet(intent.Intent, out _))
            .ToList();
        var databaseIntents = analysisIntents
            .Where(intent => !semanticIntentCatalog.TryGet(intent.Intent, out _))
            .ToList();

        logger.LogInformation(
            "数据分析意图拆分完成。语义意图: {SemanticCount}, 自由分析意图: {DatabaseCount}",
            semanticIntents.Count,
            databaseIntents.Count);

        var output = new StringBuilder();
        foreach (var intent in semanticIntents)
        {
            output.AppendLine(await semanticRunner.RunAsync(intent, ct));
        }

        foreach (var intent in databaseIntents)
        {
            output.AppendLine(await freeFormRunner.RunAsync(intent, sink, session, ct));
        }

        return BranchResult.FromDataAnalysis(output.ToString());
    }

    private static AnalysisDto BuildSemanticAnalysis(
        SemanticQueryPlan plan,
        string sourceLabel,
        SemanticSummaryDto semanticSummary,
        bool isTruncated)
    {
        return SemanticAnalysisPresentation.BuildAnalysis(plan, sourceLabel, semanticSummary, isTruncated);
    }
}
