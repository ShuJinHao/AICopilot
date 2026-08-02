using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.AiGateway.Dtos;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Logging;

namespace AICopilot.AiGatewayService.BusinessQueries;

public enum BusinessQueryExecutionStatus
{
    Succeeded = 0,
    Empty = 1,
    NeedsConfirmation = 2,
    Failed = 3
}

public sealed record BusinessQueryExecutionResult(
    BusinessQueryExecutionStatus Status,
    string? SafeContext = null,
    string? Provider = null,
    string? SourceKey = null,
    string? SourceMode = null,
    bool IsSimulation = false,
    IReadOnlyList<string>? TrustedInlineWidgetPayloads = null,
    string? FailureCode = null,
    string? SafeMessage = null)
{
    public IReadOnlyList<string> Widgets => TrustedInlineWidgetPayloads ?? [];

    public static BusinessQueryExecutionResult Success(
        string safeContext,
        string provider,
        string sourceKey,
        string sourceMode,
        IReadOnlyList<string>? widgets = null) =>
        new(
            BusinessQueryExecutionStatus.Succeeded,
            safeContext,
            provider,
            sourceKey,
            sourceMode,
            IsSimulation: false,
            widgets);

    public static BusinessQueryExecutionResult EmptyResult(
        string safeContext,
        string provider,
        string sourceKey,
        string sourceMode,
        IReadOnlyList<string>? widgets = null) =>
        Success(safeContext, provider, sourceKey, sourceMode, widgets) with
        {
            Status = BusinessQueryExecutionStatus.Empty
        };

    public static BusinessQueryExecutionResult ConfirmationRequired(
        string code,
        string safeMessage) =>
        new(
            BusinessQueryExecutionStatus.NeedsConfirmation,
            FailureCode: code,
            SafeMessage: safeMessage);

    public static BusinessQueryExecutionResult Failure(string code, string safeMessage) =>
        new(
            BusinessQueryExecutionStatus.Failed,
            FailureCode: code,
            SafeMessage: safeMessage);
}

public sealed class BusinessQueryExecutor(
    ISemanticQueryPlanner semanticQueryPlanner,
    ILogger<BusinessQueryExecutor> logger,
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

    internal bool TryConfirmPending(
        Guid sessionId,
        string userMessage,
        out BusinessQueryContext confirmed) =>
        businessQueryContextStore.TryConfirmPending(
            sessionId,
            userMessage,
            out confirmed);

    internal async Task<BusinessQueryExecutionResult> ExecuteAsync(
        Guid sessionId,
        string semanticIntent,
        string question,
        BusinessQueryContext? confirmedQuery,
        CancellationToken cancellationToken)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("Session id is required.", nameof(sessionId));
        }

        if (IsRecipeIntent(semanticIntent))
        {
            logger.LogInformation(
                "配方数据语义查询已在规划前按云端配方禁读边界拒绝。Intent: {Intent}",
                semanticIntent);
            return BusinessQueryExecutionResult.Failure(
                AppProblemCodes.CloudReadonlyIntentUnsupported,
                RecipeDataReadBoundaryMessage);
        }

        if (confirmedQuery is not null)
        {
            if (confirmedQuery.SessionId != sessionId ||
                !confirmedQuery.IsConfirmed ||
                confirmedQuery.SemanticPlan is null ||
                confirmedQuery.SourceType != DataSourceExternalSystemType.CloudReadOnly)
            {
                return BusinessQueryExecutionResult.Failure(
                    CloudAiReadProblemCodes.MissingRequiredParameter,
                    "[系统提示]: 已确认业务查询上下文与当前会话或 Cloud 数据源不匹配，系统已停止执行。");
            }

            var confirmedPlan = confirmedQuery.SemanticPlan;
            return await RunBusinessQueryProviderAsync(
                confirmedPlan,
                confirmedQuery,
                SemanticAnalysisPresentation.GetTargetLabel(confirmedPlan.Target),
                cancellationToken);
        }

        var planningResult = semanticQueryPlanner.Plan(semanticIntent, question);
        if (!planningResult.IsSuccess)
        {
            var failedTargetLabel = SemanticAnalysisPresentation.TryGetTargetLabel(semanticIntent);
            logger.LogWarning(
                "{TargetLabel}语义查询规划失败。Intent: {Intent}, Error: {Error}",
                failedTargetLabel,
                semanticIntent,
                planningResult.ErrorMessage);
            if (IsDeviceStatusIntent(semanticIntent))
            {
                return BusinessQueryExecutionResult.Failure(
                    AppProblemCodes.CloudReadonlyIntentUnsupported,
                    $"{DeviceStatusSourceUnavailableMarker}；当前问题尚未形成可执行的结构化查询，请补充或确认查询条件。");
            }

            var message = IsCloudOnlySemanticIntent(semanticIntent)
                ? $"{failedTargetLabel}查询尚未形成可执行的结构化上下文，请补充或确认查询条件。"
                : $"{failedTargetLabel}语义查询规划失败。";
            return BusinessQueryExecutionResult.Failure(
                AppProblemCodes.CloudReadonlyIntentUnsupported,
                message);
        }

        var plan = planningResult.Plan!;
        var targetLabel = SemanticAnalysisPresentation.GetTargetLabel(plan.Target);
        if (plan.Target == SemanticQueryTarget.Recipe)
        {
            logger.LogInformation(
                "配方数据语义查询已按云端配方禁读边界拒绝。Intent: {Intent}, Kind: {Kind}",
                plan.Intent,
                plan.Kind);
            return BusinessQueryExecutionResult.Failure(
                AppProblemCodes.CloudReadonlyIntentUnsupported,
                RecipeDataReadBoundaryMessage);
        }

        if (!CloudAiReadSemanticSupport.IsSupported(plan.Target))
        {
            logger.LogWarning(
                "语义查询命中了未受支持的数据目标，系统已拒绝继续执行。Intent: {Intent}, Target: {Target}, Kind: {Kind}",
                plan.Intent,
                plan.Target,
                plan.Kind);
            return BusinessQueryExecutionResult.Failure(
                AppProblemCodes.CloudReadonlyIntentUnsupported,
                $"当前不支持{targetLabel}语义数据查询。");
        }

        var requestedContext = new BusinessQueryContext(
            SessionId: sessionId,
            SourceKey: StandardBusinessDataSourceProfiles.CloudReadOnly.Code,
            DataSourceId: null,
            SourceType: DataSourceExternalSystemType.CloudReadOnly,
            Capability: BusinessDataCapabilityMapper.FromSemanticTarget(plan.Target),
            Question: question,
            SourceExplicitlySelected: false,
            Confirmation: new BusinessQueryConfirmation(false, false, false, false, false),
            SemanticPlan: plan,
            ConfirmedAtUtc: null);
        var context = businessQueryContextStore.Resolve(requestedContext);
        if (!context.IsConfirmed)
        {
            var challenge = businessQueryContextStore.BeginConfirmation(context);
            return BusinessQueryExecutionResult.ConfirmationRequired(
                CloudAiReadProblemCodes.MissingRequiredParameter,
                BuildConfirmationChallengeMessage(targetLabel, context, challenge));
        }

        return await RunBusinessQueryProviderAsync(
            plan,
            context,
            targetLabel,
            cancellationToken);
    }

    private async Task<BusinessQueryExecutionResult> RunBusinessQueryProviderAsync(
        SemanticQueryPlan plan,
        BusinessQueryContext context,
        string targetLabel,
        CancellationToken cancellationToken)
    {
        context = context.ConfirmedAtUtc.HasValue ? context : context.Confirm();
        var provider = businessQueryProviderRegistry.ResolveRequired(context);
        var providerResult = await provider.QueryAsync(context, cancellationToken);
        BusinessQueryProviderResultContract.EnsureMatches(context, provider, providerResult);

        if (providerResult.Outcome is BusinessQueryOutcome.Success or BusinessQueryOutcome.Empty)
        {
            businessQueryContextStore.Remember(context);
            var rows = providerResult.Rows.ToList();
            var semanticSummary = SemanticSummaryBuilder.Build(plan, rows) with { Scope = string.Empty };
            var analysis = SemanticAnalysisPresentation.BuildAnalysis(
                plan,
                SemanticAnalysisPresentation.BuildCloudAiReadSourceLabel(targetLabel),
                semanticSummary,
                providerResult.IsTruncated);
            var safeContext = DataAnalysisFinalContextFormatter.FormatSemantic(
                analysis,
                semanticSummary,
                rows,
                providerResult.IsTruncated,
                plan,
                providerResult.RowCount);
            var widgets = BuildTrustedDeviceLogWidgetPayloads(plan, semanticSummary, rows);
            return providerResult.Outcome == BusinessQueryOutcome.Empty
                ? BusinessQueryExecutionResult.EmptyResult(
                    safeContext,
                    providerResult.ProviderCode,
                    providerResult.SourceKey,
                    providerResult.SourceType.ToString(),
                    widgets)
                : BusinessQueryExecutionResult.Success(
                    safeContext,
                    providerResult.ProviderCode,
                    providerResult.SourceKey,
                    providerResult.SourceType.ToString(),
                    widgets);
        }

        if (providerResult.Outcome == BusinessQueryOutcome.NeedClarification)
        {
            return BusinessQueryExecutionResult.ConfirmationRequired(
                CloudAiReadProblemCodes.MissingRequiredParameter,
                $"[系统提示]: {targetLabel}查询缺少必要条件，请补充设备、时间范围或条码后重试。");
        }

        if (providerResult.Outcome == BusinessQueryOutcome.Unauthorized)
        {
            return BusinessQueryExecutionResult.Failure(
                CloudAiReadProblemCodes.Forbidden,
                $"[系统提示]: {targetLabel}查询权限或设备范围不足，系统已明确终止本次正式数据读取。");
        }

        var fallbackDecision = BusinessQueryFallbackPolicy.EvaluateSameSourceTextToSql(
            context,
            providerResult,
            businessDataSourceProfileRegistry.GetRequired(context.SourceKey, context.SourceType));
        if (fallbackDecision.IsEligible)
        {
            return await RunSameSourceTextToSqlFallbackAsync(
                context,
                plan,
                targetLabel,
                cancellationToken);
        }

        return BusinessQueryExecutionResult.Failure(
            AppProblemCodes.CloudReadonlyIntentUnsupported,
            $"[系统提示]: {targetLabel}查询已终止；Outcome={providerResult.Outcome}; Reason={fallbackDecision.ReasonCode}。");
    }

    private async Task<BusinessQueryExecutionResult> RunSameSourceTextToSqlFallbackAsync(
        BusinessQueryContext context,
        SemanticQueryPlan plan,
        string targetLabel,
        CancellationToken cancellationToken)
    {
        if (businessDatabaseReadService is null || businessTextToSqlFallbackRunner is null)
        {
            return BusinessQueryExecutionResult.Failure(
                AppProblemCodes.CloudReadonlyIntentUnsupported,
                "[系统提示]: 同源受控 Text-to-SQL 当前未配置，系统已停止执行。");
        }

        var sources = await businessDatabaseReadService.ListSelectableAsync(
            DataSourceSelectionMode.GovernedSql,
            cancellationToken);
        var matchingSources = BusinessDataSourceBindingResolver.Resolve(context, sources);
        if (matchingSources.Count != 1)
        {
            return BusinessQueryExecutionResult.Failure(
                AppProblemCodes.CloudReadonlyIntentUnsupported,
                "[系统提示]: 已确认业务查询无法绑定到唯一授权业务数据源，系统已停止且不会跨源回退。");
        }

        var source = matchingSources[0];
        var database = await businessDatabaseReadService.GetByNameAsync(source.Name, cancellationToken);
        if (database is null)
        {
            return BusinessQueryExecutionResult.Failure(
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
            return BusinessQueryExecutionResult.Failure(
                AppProblemCodes.CloudReadonlyIntentUnsupported,
                $"[系统提示]: 同源受控 Text-to-SQL 执行失败：{fallbackResult.SafeMessage}");
        }

        businessQueryContextStore.Remember(boundContext);
        logger.LogInformation(
            "{TargetLabel}结构化插件失败后已在已确认业务查询范围内执行同源 Text-to-SQL。Outcome=Succeeded; Rows={RowCount}; Truncated={IsTruncated}",
            targetLabel,
            fallbackResult.RowCount,
            fallbackResult.IsTruncated);
        return fallbackResult.RowCount == 0
            ? BusinessQueryExecutionResult.EmptyResult(
                fallbackResult.Context,
                "business-text-to-sql:v1",
                context.SourceKey,
                context.SourceType.ToString())
            : BusinessQueryExecutionResult.Success(
                fallbackResult.Context,
                "business-text-to-sql:v1",
                context.SourceKey,
                context.SourceType.ToString());
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

    private IReadOnlyList<string> BuildTrustedDeviceLogWidgetPayloads(
        SemanticQueryPlan plan,
        SemanticSummaryDto semanticSummary,
        IReadOnlyList<Dictionary<string, object?>> rows)
    {
        if (rows.Count == 0 || plan.Target != SemanticQueryTarget.DeviceLog)
        {
            return [];
        }

        try
        {
            return DeviceLogSemanticDisplayBuilder.BuildWidgets(plan, semanticSummary, rows)
                .Select(DataAnalysisWidgetPayloadSerializer.Serialize)
                .ToArray();
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "{TargetLabel}语义查询展示组件构建失败。Intent: {Intent}, Target: {Target}, Kind: {Kind}, ErrorType={ErrorType}; OriginalMessage=hidden_by_security_policy",
                SemanticAnalysisPresentation.GetTargetLabel(plan.Target),
                plan.Intent,
                plan.Target,
                plan.Kind,
                exception.GetType().Name);
            return [];
        }
    }

    private static bool IsDeviceStatusIntent(string intent) =>
        intent.Equals("Analysis.Device.Status", StringComparison.OrdinalIgnoreCase);

    private static bool IsRecipeIntent(string intent) =>
        intent.StartsWith("Analysis.Recipe.", StringComparison.OrdinalIgnoreCase);

    private static bool IsCloudOnlySemanticIntent(string intent) =>
        intent.StartsWith("Analysis.Device.", StringComparison.OrdinalIgnoreCase) ||
        intent.StartsWith("Analysis.DeviceLog.", StringComparison.OrdinalIgnoreCase) ||
        intent.StartsWith("Analysis.Capacity.", StringComparison.OrdinalIgnoreCase) ||
        intent.StartsWith("Analysis.ProductionData.", StringComparison.OrdinalIgnoreCase) ||
        intent.StartsWith("Analysis.Process.", StringComparison.OrdinalIgnoreCase) ||
        intent.StartsWith("Analysis.ClientRelease.", StringComparison.OrdinalIgnoreCase);
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
                (context.DataSourceId is null || source.Id == context.DataSourceId))
            .ToArray();
    }
}
