using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Workflows;
using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.AiGateway.Dtos;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Logging;

namespace AICopilot.AiGatewayService.Workflows.Executors;

public sealed class SemanticAnalysisRunner(
    ISemanticQueryPlanner semanticQueryPlanner,
    ILogger<SemanticAnalysisRunner> logger,
    IBusinessQueryProviderRegistry businessQueryProviderRegistry,
    IBusinessDataSourceProfileRegistry businessDataSourceProfileRegistry,
    IBusinessQueryContextStore businessQueryContextStore,
    IBusinessDatabaseReadService? businessDatabaseReadService = null,
    IBusinessTextToSqlFallbackRunner? businessTextToSqlFallbackRunner = null)
{
    public const string RecipeDataReadBoundaryMarker = "当前 AI 不读取云端配方主数据或配方版本数据";
    public const string DeviceStatusSourceUnavailableMarker = "当前设备最后上报运行状态的正式 Cloud AiRead 数据源不可用";
    private const string RecipeDataReadBoundaryMessage =
        "[系统提示]: " + RecipeDataReadBoundaryMarker + "。可以回答配方版本规则问题，但不能查询具体配方、设备配方清单或版本记录。";
    private const double MinimumConfirmedSemanticConfidence = 0.65;

    internal async Task<AgentAnalysisNodeResult> RunAsync(
        IntentResult intent,
        AgentWorkflowSink? sink,
        SessionRuntimeSnapshot? session,
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

        if (intent.ConfirmedBusinessQuery is { } confirmedBusinessQuery)
        {
            if (session is null ||
                confirmedBusinessQuery.TaskId != session.Id ||
                !confirmedBusinessQuery.IsConfirmed ||
                confirmedBusinessQuery.SemanticPlan is null ||
                confirmedBusinessQuery.SourceType != DataSourceExternalSystemType.CloudReadOnly)
            {
                return AgentAnalysisNodeResult.Failed(
                    CloudAiReadProblemCodes.MissingRequiredParameter,
                    "[系统提示]: 已确认业务查询上下文与当前会话或 Cloud 数据源不匹配，系统已停止执行。");
            }

            var confirmedPlan = confirmedBusinessQuery.SemanticPlan;
            return await RunBusinessQueryProviderAsync(
                confirmedPlan,
                intent,
                SemanticAnalysisPresentation.GetTargetLabel(confirmedPlan.Target),
                sink,
                session,
                cancellationToken);
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
                    $"{DeviceStatusSourceUnavailableMarker}；当前问题尚未形成可执行的结构化查询，请补充或确认查询条件。");
            }

            if (IsCloudOnlySemanticIntent(intent.Intent))
            {
                return AgentAnalysisNodeResult.Failed(
                    AppProblemCodes.CloudReadonlyIntentUnsupported,
                    $"{failedTargetLabel}查询尚未形成可执行的结构化上下文，请补充或确认查询条件。");
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

        return await RunBusinessQueryProviderAsync(
            plan,
            intent,
            targetLabel,
            sink,
            session,
            cancellationToken);
    }

    private async Task<AgentAnalysisNodeResult> RunBusinessQueryProviderAsync(
        SemanticQueryPlan plan,
        IntentResult routedIntent,
        string targetLabel,
        AgentWorkflowSink? sink,
        SessionRuntimeSnapshot? session,
        CancellationToken cancellationToken)
    {
        var explicitConfirmation = routedIntent.ConfirmedBusinessQueryContext;
        var requestedContext = new BusinessQueryContext(
            TaskId: session?.Id ?? Guid.Empty,
            SourceKey: StandardBusinessDataSourceProfiles.CloudReadOnly.Code,
            DataSourceId: null,
            SourceType: DataSourceExternalSystemType.CloudReadOnly,
            Capability: BusinessDataCapabilityMapper.FromSemanticTarget(plan.Target),
            Question: routedIntent.Query ?? plan.Intent,
            SourceExplicitlySelected: routedIntent.BusinessDataSourceExplicitlySelected,
            Confirmation: BusinessQueryConfirmationPolicy.FromSemanticPlan(
                sourceConfirmed: routedIntent.BusinessDataSourceExplicitlySelected &&
                                 explicitConfirmation?.Source == true,
                capabilityConfirmed: explicitConfirmation?.Capability == true,
                confidenceConfirmed: routedIntent.Confidence >= MinimumConfirmedSemanticConfidence,
                semanticPlan: plan,
                businessObjectConfirmed: explicitConfirmation?.BusinessObject == true,
                timeRangeConfirmed: explicitConfirmation?.TimeRange == true,
                filtersConfirmed: explicitConfirmation?.Filters == true),
            SemanticPlan: plan,
            ConfirmedAtUtc: null);
        var context = businessQueryContextStore.Resolve(requestedContext);
        if (!context.IsConfirmed)
        {
            if (context.TaskId != Guid.Empty && context.SemanticPlan is not null)
            {
                var challenge = businessQueryContextStore.BeginConfirmation(context);
                return AgentAnalysisNodeResult.Failed(
                    CloudAiReadProblemCodes.MissingRequiredParameter,
                    BuildConfirmationChallengeMessage(targetLabel, context, challenge));
            }

            return AgentAnalysisNodeResult.Failed(
                CloudAiReadProblemCodes.MissingRequiredParameter,
                $"[系统提示]: {targetLabel}查询的来源、数据类型、业务对象或时间条件置信度不足，请确认后再执行。");
        }

        context = context.Confirm();
        var provider = businessQueryProviderRegistry.ResolveRequired(context);
        var providerResult = await provider.QueryAsync(context, cancellationToken);
        BusinessQueryProviderResultContract.EnsureMatches(
            context,
            provider,
            providerResult);

        if (providerResult.Outcome is BusinessQueryOutcome.Success or BusinessQueryOutcome.Empty)
        {
            businessQueryContextStore.Remember(context);
            var rows = providerResult.Rows.ToList();
            var semanticSummary = SemanticSummaryBuilder.Build(plan, rows) with
            {
                Scope = string.Empty
            };
            var analysis = SemanticAnalysisPresentation.BuildAnalysis(
                plan,
                SemanticAnalysisPresentation.BuildCloudAiReadSourceLabel(targetLabel),
                semanticSummary,
                providerResult.IsTruncated);
            await TryEmitDeviceLogWidgetsAsync(plan, semanticSummary, rows, sink, cancellationToken);
            var safeContext = DataAnalysisFinalContextFormatter.FormatSemantic(
                analysis,
                semanticSummary,
                rows,
                providerResult.IsTruncated,
                plan,
                providerResult.RowCount);
            var evidence = AgentBranchEvidenceSeed.CreateObservedDataQuery(
                "CloudReadNode",
                safeContext,
                "business-query-plugin:v1",
                providerResult.ProviderCode,
                providerResult.SourceType.ToString(),
                isSimulation: false,
                plan.Intent,
                []);
            return providerResult.Outcome == BusinessQueryOutcome.Empty
                ? AgentAnalysisNodeResult.Empty(evidence)
                : AgentAnalysisNodeResult.Succeeded(evidence);
        }

        if (providerResult.Outcome == BusinessQueryOutcome.NeedClarification)
        {
            return AgentAnalysisNodeResult.Failed(
                CloudAiReadProblemCodes.MissingRequiredParameter,
                $"[系统提示]: {targetLabel}查询缺少必要条件，请补充设备、时间范围或条码后重试。");
        }

        if (providerResult.Outcome == BusinessQueryOutcome.Unauthorized)
        {
            return AgentAnalysisNodeResult.Failed(
                CloudAiReadProblemCodes.Forbidden,
                $"[系统提示]: {targetLabel}查询权限或设备范围不足，系统已明确终止本次正式数据读取。");
        }

        var fallbackDecision = BusinessQueryFallbackPolicy.EvaluateSameSourceTextToSql(
            context,
            providerResult,
            businessDataSourceProfileRegistry.GetRequired(
                context.SourceKey,
                context.SourceType));
        if (fallbackDecision.IsEligible)
        {
            return await RunSameSourceTextToSqlFallbackAsync(
                context,
                plan,
                targetLabel,
                cancellationToken);
        }

        return AgentAnalysisNodeResult.Failed(
            AppProblemCodes.CloudReadonlyIntentUnsupported,
            $"[系统提示]: {targetLabel}查询已终止；Outcome={providerResult.Outcome}; Reason={fallbackDecision.ReasonCode}。");
    }

    private async Task<AgentAnalysisNodeResult> RunSameSourceTextToSqlFallbackAsync(
        BusinessQueryContext context,
        SemanticQueryPlan plan,
        string targetLabel,
        CancellationToken cancellationToken)
    {
        if (businessDatabaseReadService is null ||
            businessTextToSqlFallbackRunner is null)
        {
            return AgentAnalysisNodeResult.Failed(
                AppProblemCodes.CloudReadonlyIntentUnsupported,
                "[系统提示]: 同源受控 Text-to-SQL 当前未配置，系统已停止执行。");
        }

        var sources = await businessDatabaseReadService.ListSelectableAsync(
            DataSourceSelectionMode.GovernedSql,
            cancellationToken);
        var matchingSources = BusinessDataSourceBindingResolver.Resolve(
            context,
            sources);
        if (matchingSources.Count != 1)
        {
            return AgentAnalysisNodeResult.Failed(
                AppProblemCodes.CloudReadonlyIntentUnsupported,
                "[系统提示]: 已确认业务查询无法绑定到唯一授权业务数据源，系统已停止且不会跨源回退。");
        }

        var source = matchingSources[0];
        var database = await businessDatabaseReadService.GetByNameAsync(
            source.Name,
            cancellationToken);
        if (database is null)
        {
            return AgentAnalysisNodeResult.Failed(
                AppProblemCodes.CloudReadonlyIntentUnsupported,
                "[系统提示]: 已确认业务数据源当前不可用，系统已停止且不会跨源回退。");
        }

        var boundContext = context with { DataSourceId = source.Id };
        var fallbackResult = await businessTextToSqlFallbackRunner.RunAsync(
            boundContext,
            database,
            context.Question,
            source.DefaultQueryLimit,
            cancellationToken);
        if (!fallbackResult.Succeeded)
        {
            return AgentAnalysisNodeResult.Failed(
                AppProblemCodes.CloudReadonlyIntentUnsupported,
                $"[系统提示]: 同源受控 Text-to-SQL 执行失败：{fallbackResult.SafeMessage}");
        }

        businessQueryContextStore.Remember(boundContext);
        var evidence = AgentBranchEvidenceSeed.CreateObservedDataQuery(
            "CloudReadNode",
            fallbackResult.Context,
            "business-text-to-sql:v1",
            context.SourceKey,
            context.SourceType.ToString(),
            isSimulation: false,
            plan.Intent,
            []);
        logger.LogInformation(
            "{TargetLabel}结构化插件失败后已在已确认业务查询范围内执行同源 Text-to-SQL。Outcome=Succeeded; Rows={RowCount}; Truncated={IsTruncated}",
            targetLabel,
            fallbackResult.RowCount,
            fallbackResult.IsTruncated);
        return fallbackResult.RowCount == 0
            ? AgentAnalysisNodeResult.Empty(evidence)
            : AgentAnalysisNodeResult.Succeeded(evidence);
    }

    private static string BuildConfirmationChallengeMessage(
        string targetLabel,
        BusinessQueryContext context,
        BusinessQueryConfirmationChallenge challenge)
    {
        var timeScope = context.SemanticPlan?.TimeRange is { } timeRange
            ? $"{timeRange.Start?.ToString("O") ?? "open"}..{timeRange.End?.ToString("O") ?? "open"}"
            : "未限定";
        var filterScope = context.SemanticPlan?.Filters.Count > 0
            ? string.Join(
                "；",
                context.SemanticPlan.Filters.Select(filter =>
                    $"{filter.Field} {filter.Operator} {filter.Value}"))
            : "无额外过滤";
        return
            $"[系统提示]: 请确认本次{targetLabel}查询范围：数据源=Cloud；数据类型={context.Capability}；业务对象={context.SemanticPlan?.Target}；时间范围={timeScope}；过滤条件={filterScope}。若确认无误，请在 {challenge.ExpiresAtUtc:O} 前仅回复“确认查询 {challenge.Token}”。";
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

internal static class BusinessDataSourceBindingResolver
{
    public static IReadOnlyList<BusinessDatabaseDescriptor> Resolve(
        BusinessQueryContext context,
        IEnumerable<BusinessDatabaseDescriptor> sources)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sources);
        return sources
            .Where(source =>
                source.ExternalSystemType == context.SourceType &&
                string.Equals(
                    BusinessDataSourceProfileKeyResolver.Resolve(source),
                    context.SourceKey,
                    StringComparison.OrdinalIgnoreCase) &&
                (context.DataSourceId is null ||
                 source.Id == context.DataSourceId))
            .ToArray();
    }
}
