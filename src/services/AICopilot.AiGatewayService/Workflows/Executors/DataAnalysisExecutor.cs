using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.AgentTasks;
using System.Text;
using AICopilot.AiGatewayService.Models;
using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.AiGateway.Dtos;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Logging;

namespace AICopilot.AiGatewayService.Workflows.Executors;

/// <summary>
/// 数据分析执行器只负责 Analysis.* 意图分派，具体执行由 semantic/free-form runner 承担。
/// </summary>
public class DataAnalysisExecutor(
    ISemanticQuerySchemaRegistry semanticQuerySchemaRegistry,
    SemanticAnalysisRunner semanticRunner,
    FreeFormDbaAnalysisRunner freeFormRunner,
    ILogger<DataAnalysisExecutor> logger)
{
    public const string ExecutorId = nameof(DataAnalysisExecutor);
    public const string AnalysisIntentPrefix = "Analysis.";

    internal static bool IsRelevant(IEnumerable<IntentResult> intents, AgentIntentRegistrySnapshot registry) =>
        AgentWorkflowIntentSelector.Any(
            intents, registry, 0.6, null,
            AgentIntentClass.CloudOnly, AgentIntentClass.GovernedExploration, AgentIntentClass.KnownButUnavailable);

    public async Task<BranchResult> ExecuteAsync(
        List<IntentResult> intentResults,
        AgentIntentRegistrySnapshot registry,
        AgentWorkflowSink? sink,
        SessionRuntimeSnapshot? session,
        CancellationToken ct = default)
    {
        var analysisIntents = AgentWorkflowIntentSelector.Select(
            intentResults, registry, 0.6, null,
            AgentIntentClass.CloudOnly, AgentIntentClass.GovernedExploration, AgentIntentClass.KnownButUnavailable);

        if (analysisIntents.Count == 0)
        {
            logger.LogDebug("未检测到数据分析意图，跳过执行。");
            return BranchResult.Skipped(BranchType.DataAnalysis);
        }

        logger.LogInformation("启动数据分析流程，命中 Analysis.* 意图数量: {Count}", analysisIntents.Count);

        var semanticIntents = analysisIntents
            .Where(intent => semanticQuerySchemaRegistry.TryGet(intent.Intent, out _))
            .ToList();
        var databaseIntents = analysisIntents
            .Where(intent => !semanticQuerySchemaRegistry.TryGet(intent.Intent, out _))
            .ToList();

        logger.LogInformation(
            "数据分析意图拆分完成。语义意图: {SemanticCount}, 自由分析意图: {DatabaseCount}",
            semanticIntents.Count,
            databaseIntents.Count);

        var nodeResults = new List<AgentAnalysisNodeResult>();
        foreach (var intent in semanticIntents)
        {
            nodeResults.Add(await semanticRunner.RunAsync(intent, sink, ct));
        }

        foreach (var intent in databaseIntents)
        {
            nodeResults.Add(await freeFormRunner.RunAsync(intent, sink, session, ct));
        }

        var failed = nodeResults.FirstOrDefault(result => result.Status == BranchExecutionStatus.Failed);
        if (failed is not null)
        {
            return BranchResult.Failed(
                BranchType.DataAnalysis,
                failed.FailureCode ?? AppProblemCodes.ChatStreamFailed,
                failed.SafeMessage ?? "A required data-analysis node failed.");
        }

        var succeededEvidence = nodeResults
            .Where(result => result.Status == BranchExecutionStatus.Succeeded && result.Evidence is not null)
            .Select(result => result.Evidence!)
            .ToArray();
        if (succeededEvidence.Length == 0)
        {
            return BranchResult.Empty(BranchType.DataAnalysis);
        }

        var output = new StringBuilder();
        foreach (var evidence in succeededEvidence)
        {
            output.AppendLine(evidence.SafeContext);
        }

        return BranchResult.FromDataAnalysis(output.ToString(), succeededEvidence);
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
