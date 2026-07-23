using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Workflows;
using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.AiGateway.Dtos;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Logging;

namespace AICopilot.AiGatewayService.Workflows.Executors;

public sealed class SemanticAnalysisRunner(
    ICloudAiReadClient cloudAiReadClient,
    ISemanticQueryPlanner semanticQueryPlanner,
    ILogger<SemanticAnalysisRunner> logger)
{
    public const string RecipeDataReadBoundaryMarker = "当前 AI 不读取云端配方主数据或配方版本数据";
    public const string DeviceStatusSourceUnavailableMarker = "当前设备最后上报运行状态的正式 Cloud AiRead 数据源不可用";
    public const string CloudOnlySemanticSourceUnavailableMarker = "当前正式 Cloud AiRead 数据源不可用";
    private const string RecipeDataReadBoundaryMessage =
        "[系统提示]: " + RecipeDataReadBoundaryMarker + "。可以回答配方版本规则问题，但不能查询具体配方、设备配方清单或版本记录。";

    internal async Task<AgentAnalysisNodeResult> RunAsync(
        IntentResult intent,
        AgentWorkflowSink? sink,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intent);

        if (IsRecipeIntent(intent.Intent))
        {
            logger.LogInformation(
                "配方数据语义查询已在规划前按云端配方禁读边界拒绝。Intent: {Intent}",
                intent.Intent);
            return AgentAnalysisNodeResult.Failed(
                AppProblemCodes.CloudReadonlyIntentUnsupported,
                RecipeDataReadBoundaryMessage);
        }

        var planningResult = semanticQueryPlanner.Plan(intent.Intent, intent.Query);
        if (!planningResult.IsSuccess)
        {
            var failedTargetLabel = SemanticAnalysisPresentation.TryGetTargetLabel(intent.Intent);
            logger.LogWarning(
                "{TargetLabel}语义查询规划失败。Intent: {Intent}, Error: {Error}",
                failedTargetLabel,
                intent.Intent,
                planningResult.ErrorMessage);
            if (IsDeviceStatusIntent(intent.Intent))
            {
                return AgentAnalysisNodeResult.Failed(
                    AppProblemCodes.CloudReadonlyIntentUnsupported,
                    $"{DeviceStatusSourceUnavailableMarker}，且该能力不会回退 Direct DB、Text-to-SQL 或 Simulation。");
            }

            if (IsCloudOnlySemanticIntent(intent.Intent))
            {
                return AgentAnalysisNodeResult.Failed(
                    AppProblemCodes.CloudReadonlyIntentUnsupported,
                    $"{failedTargetLabel}{CloudOnlySemanticSourceUnavailableMarker}，且该能力不会回退 Direct DB、Text-to-SQL 或 Simulation。");
            }

            return AgentAnalysisNodeResult.Failed(
                AppProblemCodes.CloudReadonlyIntentUnsupported,
                $"{failedTargetLabel}语义查询规划失败。");
        }

        var plan = planningResult.Plan!;
        var targetLabel = SemanticAnalysisPresentation.GetTargetLabel(plan.Target);
        if (plan.Target == SemanticQueryTarget.Recipe)
        {
            logger.LogInformation(
                "配方数据语义查询已按云端配方禁读边界拒绝。Intent: {Intent}, Kind: {Kind}",
                plan.Intent,
                plan.Kind);
            return AgentAnalysisNodeResult.Failed(
                AppProblemCodes.CloudReadonlyIntentUnsupported,
                RecipeDataReadBoundaryMessage);
        }

        if (!IsCloudOnlySemanticTarget(plan.Target))
        {
            logger.LogWarning(
                "语义查询命中了未受支持的数据目标，系统已拒绝继续执行。Intent: {Intent}, Target: {Target}, Kind: {Kind}",
                plan.Intent,
                plan.Target,
                plan.Kind);
            return AgentAnalysisNodeResult.Failed(
                AppProblemCodes.CloudReadonlyIntentUnsupported,
                $"当前不支持{targetLabel}语义数据查询。");
        }

        if (!cloudAiReadClient.IsEnabled)
        {
            if (plan.Target == SemanticQueryTarget.Device && plan.Kind == SemanticQueryKind.Status)
            {
                return AgentAnalysisNodeResult.Failed(
                    AppProblemCodes.CloudReadonlyIntentUnsupported,
                    $"{DeviceStatusSourceUnavailableMarker}；该能力不会回退 Direct DB、Text-to-SQL 或 Simulation。");
            }

            return AgentAnalysisNodeResult.Failed(
                AppProblemCodes.CloudReadonlyIntentUnsupported,
                $"{targetLabel}{CloudOnlySemanticSourceUnavailableMarker}；该能力不会回退 Direct DB、Text-to-SQL 或 Simulation。");
        }

        return await RunCloudAiReadAsync(plan, targetLabel, sink, cancellationToken);
    }

    private async Task<AgentAnalysisNodeResult> RunCloudAiReadAsync(
        SemanticQueryPlan plan,
        string targetLabel,
        AgentWorkflowSink? sink,
        CancellationToken cancellationToken)
    {
        try
        {
            var queryResult = await cloudAiReadClient.QuerySemanticAsync(plan, cancellationToken);
            var rows = queryResult.Rows.ToList();
            var semanticSummary = SemanticSummaryBuilder.Build(plan, rows) with
            {
                Scope = queryResult.QueryScope
            };
            var analysis = SemanticAnalysisPresentation.BuildAnalysis(
                plan,
                SemanticAnalysisPresentation.BuildCloudAiReadSourceLabel(targetLabel),
                semanticSummary,
                queryResult.IsTruncated);

            logger.LogInformation(
                "{TargetLabel}语义查询已通过 Cloud AiRead API 完成。Intent: {Intent}, SourcePath: {SourcePath}, RowsObserved: {RowsObserved}, Truncated: {Truncated}",
                targetLabel,
                plan.Intent,
                queryResult.SourcePath,
                rows.Count,
                queryResult.IsTruncated);

            await TryEmitDeviceLogWidgetsAsync(plan, semanticSummary, rows, sink, cancellationToken);
            var safeContext = DataAnalysisFinalContextFormatter.FormatSemantic(
                analysis,
                semanticSummary,
                rows,
                queryResult.IsTruncated,
                plan,
                queryResult.RowCount);
            var evidence = new AgentBranchEvidenceSeed(
                "CloudReadNode",
                safeContext,
                AgentWorkflowEvidenceKind.DataQuery,
                AgentWorkflowEvidenceTruthClass.ObservedFact,
                "cloud-ai-read:v1",
                "CloudAiRead",
                "CloudReadOnly",
                IsSimulation: false,
                plan.Intent,
                string.IsNullOrWhiteSpace(queryResult.QueryScope)
                    ? []
                    : [queryResult.QueryScope]);
            return queryResult.RowCount == 0
                ? AgentAnalysisNodeResult.Empty(evidence)
                : AgentAnalysisNodeResult.Succeeded(evidence);
        }
        catch (CloudAiReadException ex)
        {
            logger.LogWarning(
                "{TargetLabel} Cloud AiRead 查询被拒绝或暂不可用。Intent: {Intent}, Code: {Code}; ErrorType={ErrorType}; OriginalMessage=hidden_by_security_policy",
                targetLabel,
                plan.Intent,
                ex.Code,
                ex.GetType().Name);
            var safeMessage = ex.Code switch
            {
                CloudAiReadProblemCodes.MissingRequiredParameter => $"[系统提示]: Cloud AiRead {targetLabel}查询缺少必要条件，请补充设备、时间范围或条码后重试。",
                CloudAiReadProblemCodes.InvalidRequest => $"[系统提示]: Cloud AiRead {targetLabel}查询参数不符合正式接口契约，请调整查询条件后重试。",
                CloudAiReadProblemCodes.Unauthorized => $"[系统提示]: Cloud AiRead {targetLabel}查询未通过身份凭据校验，请联系管理员检查只读服务账号。",
                CloudAiReadProblemCodes.Forbidden => $"[系统提示]: Cloud AiRead {targetLabel}查询权限或设备范围不足，系统已拒绝本次正式数据读取。",
                CloudAiReadProblemCodes.RateLimited => $"[系统提示]: Cloud AiRead {targetLabel}查询当前受到限流，请稍后重试。",
                CloudAiReadProblemCodes.RequestBlocked => $"[系统提示]: Cloud AiRead {targetLabel}查询未通过只读白名单校验，系统已拒绝执行。",
                _ => $"[系统提示]: Cloud AiRead {targetLabel}只读接口暂不可用，请稍后重试或联系管理员检查配置。"
            };
            return AgentAnalysisNodeResult.Failed(ex.Code, safeMessage);
        }
    }

    private async Task TryEmitDeviceLogWidgetsAsync(
        SemanticQueryPlan plan,
        SemanticSummaryDto semanticSummary,
        IReadOnlyList<Dictionary<string, object?>> rows,
        AgentWorkflowSink? sink,
        CancellationToken cancellationToken)
    {
        if (sink is null || plan.Target != SemanticQueryTarget.DeviceLog)
        {
            return;
        }

        try
        {
            foreach (var widget in DeviceLogSemanticDisplayBuilder.BuildWidgets(plan, semanticSummary, rows))
            {
                await sink.WriteAsync(
                    new ChatChunk(
                        DataAnalysisExecutor.ExecutorId,
                        ChunkType.Widget,
                        DataAnalysisWidgetPayloadSerializer.Serialize(widget)),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "{TargetLabel}语义查询展示组件构建失败。Intent: {Intent}, Target: {Target}, Kind: {Kind}, ErrorType={ErrorType}; OriginalMessage=hidden_by_security_policy",
                SemanticAnalysisPresentation.GetTargetLabel(plan.Target),
                plan.Intent,
                plan.Target,
                plan.Kind,
                ex.GetType().Name);
        }
    }

    private static bool IsDeviceStatusIntent(string intent)
    {
        return intent.Equals("Analysis.Device.Status", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRecipeIntent(string intent)
    {
        return intent.StartsWith("Analysis.Recipe.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCloudOnlySemanticIntent(string intent)
    {
        return intent.StartsWith("Analysis.Device.", StringComparison.OrdinalIgnoreCase) ||
               intent.StartsWith("Analysis.DeviceLog.", StringComparison.OrdinalIgnoreCase) ||
               intent.StartsWith("Analysis.Capacity.", StringComparison.OrdinalIgnoreCase) ||
               intent.StartsWith("Analysis.ProductionData.", StringComparison.OrdinalIgnoreCase) ||
               intent.StartsWith("Analysis.Process.", StringComparison.OrdinalIgnoreCase) ||
               intent.StartsWith("Analysis.ClientRelease.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCloudOnlySemanticTarget(SemanticQueryTarget target)
    {
        return CloudAiReadSemanticSupport.IsSupported(target);
    }
}
