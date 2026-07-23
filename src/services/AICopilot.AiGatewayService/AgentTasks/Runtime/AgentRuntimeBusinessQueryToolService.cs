using System.Security.Cryptography;
using System.Text;
using AICopilot.AiGatewayService.Workflows.Executors;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed class AgentRuntimeBusinessQueryToolService(
    IBusinessDatabaseReadService? businessDatabaseReadService,
    IBusinessTextToSqlRuntime? businessTextToSqlRuntime,
    BusinessTextToSqlFallbackRunner? businessTextToSqlFallbackRunner,
    IBusinessDataSourceProfileRegistry profileRegistry,
    IBusinessQueryProviderRegistry providerRegistry,
    IBusinessQueryContextStore queryContextStore)
{
    private const double MinimumConfirmedSemanticConfidence = 0.65;

    public Task<object> QueryBusinessDatabaseReadonlyP1Async(
        AgentTaskPlanDocument plan,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        return QueryBusinessDatabaseReadonlyCoreAsync(
            plan.Goal,
            plan.PlanId ?? Guid.Empty,
            plan,
            ResolveSingleSemanticIntent(plan),
            state,
            cancellationToken);
    }

    public Task<object> QueryBusinessDatabaseReadonlyP1Async(
        AgentTask task,
        AgentTaskPlanDocument plan,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        return QueryBusinessDatabaseReadonlyCoreAsync(
            task.Goal,
            task.Id.Value,
            plan,
            ResolveSingleSemanticIntent(plan),
            state,
            cancellationToken);
    }

    public Task<object> QueryBusinessDatabaseReadonlyP1Async(
        AgentTask task,
        AgentTaskPlanDocument plan,
        AgentStep step,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        return QueryBusinessDatabaseReadonlyCoreAsync(
            task.Goal,
            task.Id.Value,
            plan,
            ResolveBoundSemanticIntent(plan, step),
            state,
            cancellationToken);
    }

    private async Task<object> QueryBusinessDatabaseReadonlyCoreAsync(
        string taskGoal,
        Guid taskId,
        AgentTaskPlanDocument plan,
        AgentTaskPlanCloudReadonlyIntentDocument? semanticIntent,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        if (businessDatabaseReadService is null)
        {
            throw new InvalidOperationException("Business database read service is not configured.");
        }

        var enabledSources = await businessDatabaseReadService.ListSelectableAsync(
            DataSourceSelectionMode.Agent,
            cancellationToken);
        var selectedIds = (plan.DataSourceIds ?? [])
            .Where(id => id != Guid.Empty)
            .ToHashSet();
        var selectedDomains = (plan.BusinessDomains ?? [])
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Select(domain => domain.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedCapability = TryResolveCapability(selectedDomains);
        var selectedSourceFilters = selectedDomains
            .Where(domain => !Enum.TryParse<BusinessDataCapability>(
                domain,
                ignoreCase: true,
                out _))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = enabledSources
            .Where(item => item.IsSelectableInAgent)
            .Where(item => selectedIds.Count == 0 || selectedIds.Contains(item.Id))
            .Where(item => selectedSourceFilters.Count == 0 ||
                           selectedSourceFilters.Contains(item.BusinessDomain) ||
                           selectedSourceFilters.Contains(item.Category))
            .ToArray();

        var simulationOnly = plan.PlannerSafetySummary?.IsSimulationOnly == true;
        if (simulationOnly && selectedIds.Count != 1)
        {
            throw new InvalidOperationException(
                "SimulationBusiness execution requires exactly one data source explicitly selected in the confirmed plan.");
        }

        var simulationSource = candidates
            .Where(item => item.ExternalSystemType == DataSourceExternalSystemType.SimulationBusiness)
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        BusinessDatabaseDescriptor? source;
        if (simulationOnly)
        {
            source = simulationSource;
        }
        else if (selectedIds.Count > 0)
        {
            source = candidates.Length == 1 ? candidates.Single() : null;
        }
        else
        {
            var defaultCloudSources = candidates
                .Where(item => item.ExternalSystemType == DataSourceExternalSystemType.CloudReadOnly)
                .ToArray();
            source = defaultCloudSources.Length == 1
                ? defaultCloudSources.Single()
                : null;
        }

        if (source is null)
        {
            throw new InvalidOperationException(simulationOnly
                ? "The confirmed SimulationBusiness source is no longer available; Cloud/Real fallback is forbidden."
                : "The confirmed business query does not resolve to exactly one authorized data source.");
        }

        var sourceWasExplicitlySelected =
            selectedIds.Contains(source.Id);
        if (source.ExternalSystemType == DataSourceExternalSystemType.SimulationBusiness &&
            !sourceWasExplicitlySelected)
        {
            throw new InvalidOperationException(
                "SimulationBusiness must be explicitly selected for this agent task; implicit Simulation fallback is forbidden.");
        }

        if (source.ExternalSystemType != DataSourceExternalSystemType.SimulationBusiness)
        {
            return await QueryGovernedBusinessSourceAsync(
                source,
                taskGoal,
                taskId,
                plan,
                semanticIntent,
                selectedCapability,
                sourceWasExplicitlySelected,
                state,
                cancellationToken);
        }

        if (!IsTextToSqlFallbackSelected(plan))
        {
            throw new InvalidOperationException(
                "The confirmed plan did not explicitly select the SimulationBusiness Text-to-SQL execution mode.");
        }

        if (businessTextToSqlRuntime is null)
        {
            throw new InvalidOperationException("Business Text-to-SQL runtime is not configured.");
        }

        var draftResult = await businessTextToSqlRuntime.GenerateDraftAsync(
            new BusinessTextToSqlDraftRequest(
                source.Id,
                taskGoal,
                plan.BusinessDomains,
                source.DefaultQueryLimit,
                PreviewOnly: false),
            cancellationToken);
        var draft = RequireResultValue(draftResult, "draft");
        RequireSimulationSourceBinding(
            simulationOnly, source.Id, draft.DataSourceId, draft.SourceMode, draft.IsSimulation,
            "The SimulationBusiness draft is unmarked or bound to a different source; execution stopped without fallback.");

        var queryResult = await businessTextToSqlRuntime.ExecuteAsync(
            new BusinessTextToSqlExecuteRequest(DraftId: draft.DraftId, RequestedLimit: source.DefaultQueryLimit),
            cancellationToken);
        var result = RequireResultValue(queryResult, "execution");
        RequireSimulationSourceBinding(
            simulationOnly, source.Id, result.DataSourceId, result.SourceMode, result.IsSimulation,
            "The SimulationBusiness runtime returned an unmarked or mismatched source; execution stopped without fallback.");

        var rows = result.Rows
            .Select(row => row.ToDictionary(item => item.Key, item => item.Value))
            .ToArray();

        state.CloudReadonlySummary =
            $"BusinessDatabase Text-to-SQL executed. sourceType=BusinessDatabase; sourceMode={result.SourceMode}; isSimulation={result.IsSimulation.ToString().ToLowerInvariant()}; sourceLabel={result.SourceLabel}; queryHash={result.QueryHash}; rows={result.RowCount}; truncated={result.IsTruncated.ToString().ToLowerInvariant()}.";
        state.CloudReadonlyRows = rows;
        state.CloudReadonlySourceLabel = result.SourceLabel;
        state.CloudReadonlySourcePath = "BusinessDataSourceCenter/TextToSql";
        state.CloudReadonlySourceMode = result.SourceMode.ToString();
        state.CloudReadonlyIsSimulation = result.IsSimulation;
        state.CloudReadonlyRowCount = result.RowCount;
        state.CloudReadonlyIsTruncated = result.IsTruncated;
        state.BusinessQueryHash = result.QueryHash;
        state.BusinessQueryResults.Add(new AgentBusinessQuerySummary(
            result.DataSourceId,
            result.DataSourceName,
            result.SourceMode.ToString(),
            result.IsSimulation,
            result.SourceLabel,
            result.QueryHash,
            result.RowCount,
            result.IsTruncated,
            ArtifactId: null));

        return new
        {
            status = "completed",
            resultType = "business-query-summary",
            sourceMode = result.SourceMode.ToString(),
            isSimulation = result.IsSimulation,
            rowCount = result.RowCount,
            isTruncated = result.IsTruncated,
            resultHash = result.QueryHash
        };
    }

    private async Task<object> QueryGovernedBusinessSourceAsync(
        BusinessDatabaseDescriptor source,
        string taskGoal,
        Guid taskId,
        AgentTaskPlanDocument plan,
        AgentTaskPlanCloudReadonlyIntentDocument? semanticIntent,
        BusinessDataCapability? selectedCapability,
        bool sourceWasExplicitlySelected,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        if (businessDatabaseReadService is null)
        {
            throw new InvalidOperationException("Business database read service is not configured.");
        }

        var sourceKey = source.ExternalSystemType == DataSourceExternalSystemType.CloudReadOnly
            ? StandardBusinessDataSourceProfiles.CloudReadOnly.Code
            : source.Name;
        var requestedProfile = ResolveProfile(
            sourceKey,
            source.ExternalSystemType);
        if (requestedProfile.RequiresExplicitSelection &&
            !sourceWasExplicitlySelected)
        {
            throw new InvalidOperationException(
                "The selected business data-source profile requires explicit source selection.");
        }

        var requestedCapability = semanticIntent is null
            ? selectedCapability
            : BusinessDataCapabilityMapper.FromSemanticTarget(semanticIntent.Target);
        var semanticPlan = semanticIntent?.ToSemanticPlan();
        var confirmation = BuildConfirmation(
            plan.IsExecutable,
            requestedCapability,
            semanticIntent is { Confidence: >= MinimumConfirmedSemanticConfidence },
            semanticPlan);
        var requestedContext = new BusinessQueryContext(
            taskId,
            requestedProfile.Code,
            source.Id,
            source.ExternalSystemType,
            requestedCapability ?? BusinessDataCapability.ProductionRecord,
            taskGoal,
            sourceWasExplicitlySelected,
            confirmation,
            SemanticPlan: semanticPlan);
        var context = queryContextStore.Resolve(requestedContext);

        BusinessQueryProviderResult pluginResult;
        if (!context.IsConfirmed)
        {
            pluginResult = BusinessQueryProviderResult.FromOutcome(
                context,
                "semantic-context-validation",
                BusinessQueryOutcome.NeedClarification,
                $"Please confirm the missing business query context fields: {string.Join(", ", context.Confirmation.MissingFields())}.");
        }
        else
        {
            context = context.Confirm();
            var provider = providerRegistry.ResolveRequired(context);
            pluginResult = await provider.QueryAsync(context, cancellationToken);
            BusinessQueryProviderResultContract.EnsureMatches(context, provider, pluginResult);
        }

        if (pluginResult.Outcome is BusinessQueryOutcome.Success or BusinessQueryOutcome.Empty)
        {
            RememberConfirmedContext(context);
            return ApplyProviderResult(source, context, pluginResult, state);
        }

        if (pluginResult.Outcome == BusinessQueryOutcome.NeedClarification)
        {
            return ApplyNeedClarificationResult(source, context, pluginResult, state);
        }

        if (pluginResult.Outcome == BusinessQueryOutcome.Unauthorized)
        {
            throw new UnauthorizedAccessException(
                $"Business query provider explicitly denied the confirmed source. Provider={pluginResult.ProviderCode}; Outcome={pluginResult.Outcome}.");
        }

        var sourceProfile = ResolveProfile(context.SourceKey, context.SourceType);
        var fallbackDecision = BusinessQueryFallbackPolicy.EvaluateSameSourceTextToSql(
            context,
            pluginResult,
            sourceProfile);
        if (!fallbackDecision.IsEligible)
        {
            throw new InvalidOperationException(
                $"Business query plugin stopped without Text-to-SQL fallback. Outcome={pluginResult.Outcome}; Reason={fallbackDecision.ReasonCode}.");
        }

        if (!IsTextToSqlFallbackSelected(plan))
        {
            throw new InvalidOperationException(
                $"Same-source Text-to-SQL fallback is eligible but was not selected by the confirmed plan. Reason={fallbackDecision.ReasonCode}.");
        }

        if (businessTextToSqlFallbackRunner is null)
        {
            throw new InvalidOperationException("Governed business Text-to-SQL runtime is not configured.");
        }

        var database = await businessDatabaseReadService.GetByNameAsync(source.Name, cancellationToken);
        if (database is null)
        {
            throw new InvalidOperationException(
                "The selected governed data source is no longer available for this agent task.");
        }

        var fallbackResult = await businessTextToSqlFallbackRunner.RunAsync(
            context,
            database,
            taskGoal,
            source.DefaultQueryLimit,
            cancellationToken);
        if (!fallbackResult.Succeeded)
        {
            throw new InvalidOperationException($"Governed Text-to-SQL fallback failed: {fallbackResult.SafeMessage}");
        }

        RememberConfirmedContext(context);
        var rows = fallbackResult.Rows
            .Select(row => row.ToDictionary(item => item.Key, item => item.Value))
            .ToArray();
        var sourceLabel = $"{source.Name}（受控 Text-to-SQL 补充分析）";

        state.CloudReadonlySummary =
            $"BusinessDatabase governed Text-to-SQL executed. sourceType=BusinessDatabase; sourceMode={context.SourceType}; isSimulation=false; sourceLabel={sourceLabel}; queryHash={fallbackResult.QueryHash}; rows={fallbackResult.RowCount}; truncated={fallbackResult.IsTruncated.ToString().ToLowerInvariant()}; repairAttempts={fallbackResult.RepairAttempts.Count}.";
        state.CloudReadonlyRows = rows;
        state.CloudReadonlySourceLabel = sourceLabel;
        state.CloudReadonlySourcePath = "BusinessDataSourceCenter/GovernedTextToSql";
        state.CloudReadonlySourceMode = context.SourceType.ToString();
        state.CloudReadonlyIsSimulation = false;
        state.CloudReadonlyRowCount = fallbackResult.RowCount;
        state.CloudReadonlyIsTruncated = fallbackResult.IsTruncated;
        state.BusinessQueryHash = fallbackResult.QueryHash;
        state.BusinessQueryResults.Add(new AgentBusinessQuerySummary(
            source.Id,
            source.Name,
            context.SourceType.ToString(),
            IsSimulation: false,
            sourceLabel,
            fallbackResult.QueryHash,
            fallbackResult.RowCount,
            fallbackResult.IsTruncated,
            ArtifactId: null));

        return new
        {
            status = "completed",
            resultType = "business-query-summary",
            sourceMode = context.SourceType.ToString(),
            isSimulation = false,
            rowCount = fallbackResult.RowCount,
            isTruncated = fallbackResult.IsTruncated,
            resultHash = fallbackResult.QueryHash
        };
    }

    private BusinessDataSourceProfile ResolveProfile(
        string sourceKey,
        DataSourceExternalSystemType sourceType)
    {
        return profileRegistry.GetRequired(sourceKey, sourceType);
    }

    private static BusinessDataCapability? TryResolveCapability(
        IReadOnlyCollection<string>? businessDomains)
    {
        var capabilities = new HashSet<BusinessDataCapability>();
        foreach (var domain in businessDomains ?? [])
        {
            if (Enum.TryParse<BusinessDataCapability>(
                    domain,
                    ignoreCase: true,
                    out var capability))
            {
                capabilities.Add(capability);
            }
        }

        return capabilities.Count == 1
            ? capabilities.Single()
            : null;
    }

    private void RememberConfirmedContext(BusinessQueryContext context)
    {
        if (context.IsConfirmed)
        {
            queryContextStore.Remember(context);
        }
    }

    private static BusinessQueryConfirmation BuildConfirmation(
        bool planIsExecutable,
        BusinessDataCapability? capability,
        bool confidenceConfirmed,
        SemanticQueryPlan? semanticPlan)
    {
        return BusinessQueryConfirmationPolicy.FromSemanticPlan(
            sourceConfirmed: planIsExecutable,
            capabilityConfirmed: capability is not null,
            confidenceConfirmed: confidenceConfirmed,
            semanticPlan: semanticPlan,
            businessObjectConfirmed: planIsExecutable && semanticPlan is not null,
            timeRangeConfirmed: planIsExecutable && semanticPlan is not null,
            filtersConfirmed: planIsExecutable && semanticPlan is not null);
    }

    private static object ApplyNeedClarificationResult(
        BusinessDatabaseDescriptor source,
        BusinessQueryContext context,
        BusinessQueryProviderResult result,
        AgentTaskRunState state)
    {
        state.CloudReadonlySummary =
            $"Business query requires clarification. provider={result.ProviderCode}; sourceMode={result.SourceType}; outcome={result.Outcome}.";
        state.CloudReadonlyRows = [];
        state.CloudReadonlySourceLabel = source.Name;
        state.CloudReadonlySourcePath = result.ProviderCode;
        state.CloudReadonlySourceMode = context.SourceType.ToString();
        state.CloudReadonlyIsSimulation = false;
        state.CloudReadonlyRowCount = 0;
        state.CloudReadonlyIsTruncated = false;

        return new
        {
            status = "needs-clarification",
            resultType = "business-query-guidance",
            outcome = result.Outcome.ToString(),
            sourceMode = context.SourceType.ToString(),
            safeMessage = result.SafeMessage,
            missingFields = context.Confirmation.MissingFields()
        };
    }

    private static object ApplyProviderResult(
        BusinessDatabaseDescriptor source,
        BusinessQueryContext context,
        BusinessQueryProviderResult result,
        AgentTaskRunState state)
    {
        var rows = result.Rows
            .Select(row => row.ToDictionary(item => item.Key, item => item.Value))
            .ToArray();
        var queryHash = ComputeHash(
            $"{context.SourceKey}|{context.Capability}|{context.Question}");
        var sourceLabel = string.IsNullOrWhiteSpace(result.SourceLabel)
            ? source.Name
            : result.SourceLabel;
        var sourcePath = string.IsNullOrWhiteSpace(result.SourcePath)
            ? result.ProviderCode
            : result.SourcePath;

        state.CloudReadonlySummary =
            $"Business query plugin completed. provider={result.ProviderCode}; sourceMode={result.SourceType}; outcome={result.Outcome}; rows={result.RowCount}; truncated={result.IsTruncated.ToString().ToLowerInvariant()}.";
        state.CloudReadonlyRows = rows;
        state.CloudReadonlySourceLabel = sourceLabel;
        state.CloudReadonlySourcePath = sourcePath;
        state.CloudReadonlySourceMode = result.SourceType.ToString();
        state.CloudReadonlyIsSimulation = false;
        state.CloudReadonlyRowCount = result.RowCount;
        state.CloudReadonlyIsTruncated = result.IsTruncated;
        state.CloudReadonlyQueriedAtUtc = result.QueriedAtUtc;
        state.BusinessQueryHash = queryHash;
        state.BusinessQueryResults.Add(new AgentBusinessQuerySummary(
            source.Id,
            source.Name,
            result.SourceType.ToString(),
            IsSimulation: false,
            sourceLabel,
            queryHash,
            result.RowCount,
            result.IsTruncated,
            ArtifactId: null));

        return new
        {
            status = "completed",
            resultType = "business-query-summary",
            outcome = result.Outcome.ToString(),
            sourceMode = result.SourceType.ToString(),
            isSimulation = false,
            rowCount = result.RowCount,
            isTruncated = result.IsTruncated,
            resultHash = queryHash
        };
    }

    private static bool IsTextToSqlFallbackSelected(AgentTaskPlanDocument plan)
    {
        return string.Equals(plan.QueryMode, "TextToSql", StringComparison.Ordinal);
    }

    private static AgentTaskPlanCloudReadonlyIntentDocument? ResolveSingleSemanticIntent(
        AgentTaskPlanDocument plan)
    {
        return plan.CloudReadonlyIntents?.Count == 1
            ? plan.CloudReadonlyIntents.Single()
            : null;
    }

    private static AgentTaskPlanCloudReadonlyIntentDocument? ResolveBoundSemanticIntent(
        AgentTaskPlanDocument plan,
        AgentStep step)
    {
        var node = plan.Nodes?.ElementAtOrDefault(step.StepIndex - 1);
        if (node?.Input is not
            {
                SemanticIntent: { } semanticIntent,
                SemanticPlanDigest: { } semanticPlanDigest
            })
        {
            return null;
        }

        return plan.CloudReadonlyIntents?.SingleOrDefault(candidate =>
            string.Equals(candidate.Intent, semanticIntent, StringComparison.Ordinal) &&
            string.Equals(
                candidate.SemanticPlanDigest,
                semanticPlanDigest,
                StringComparison.Ordinal));
    }

    public object SummarizeBusinessQueryResult(AgentTaskRunState state)
    {
        if (string.IsNullOrWhiteSpace(state.CloudReadonlySourceMode) ||
            string.IsNullOrWhiteSpace(state.BusinessQueryHash))
        {
            throw new InvalidOperationException("BusinessDatabase readonly query result is not available.");
        }

        return new
        {
            status = "completed",
            resultType = "business-query-summary",
            sourceMode = state.CloudReadonlySourceMode,
            isSimulation = state.CloudReadonlyIsSimulation,
            rowCount = state.CloudReadonlyRowCount,
            isTruncated = state.CloudReadonlyIsTruncated,
            resultHash = state.BusinessQueryHash
        };
    }

    private static string BuildResultErrorSummary(IResult result)
    {
        return result.Errors is null
            ? result.Status.ToString()
            : string.Join("; ", result.Errors.Select(error => error?.ToString()).Where(error => !string.IsNullOrWhiteSpace(error)));
    }

    private static T RequireResultValue<T>(Result<T> result, string stage)
    {
        if (!result.IsSuccess || result.Value is null)
        {
            throw new InvalidOperationException(
                $"Business Text-to-SQL {stage} failed: {BuildResultErrorSummary(result)}");
        }

        return result.Value;
    }

    private static void RequireSimulationSourceBinding(
        bool simulationOnly,
        Guid expectedSourceId,
        Guid actualSourceId,
        DataSourceExternalSystemType sourceMode,
        bool isSimulation,
        string failureMessage)
    {
        if (simulationOnly &&
            (actualSourceId != expectedSourceId ||
             sourceMode != DataSourceExternalSystemType.SimulationBusiness ||
             !isSimulation))
        {
            throw new InvalidOperationException(failureMessage);
        }
    }

    private static string ComputeHash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
