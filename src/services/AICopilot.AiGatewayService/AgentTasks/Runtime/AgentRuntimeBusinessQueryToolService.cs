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
    CloudReadOnlyTextToSqlFallbackRunner? cloudTextToSqlFallbackRunner)
{
    public Task<object> QueryBusinessDatabaseReadonlyP1Async(
        AgentTaskPlanDocument plan,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        return QueryBusinessDatabaseReadonlyCoreAsync(
            plan.Goal,
            plan,
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
            plan,
            state,
            cancellationToken);
    }

    private async Task<object> QueryBusinessDatabaseReadonlyCoreAsync(
        string taskGoal,
        AgentTaskPlanDocument plan,
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

        var candidates = enabledSources
            .Where(item => item.IsSelectableInAgent)
            .Where(item => selectedIds.Count == 0 || selectedIds.Contains(item.Id))
            .Where(item => selectedDomains.Count == 0 ||
                           selectedDomains.Contains(item.BusinessDomain) ||
                           selectedDomains.Contains(item.Category))
            .ToArray();

        var simulationOnly = plan.PlannerSafetySummary?.IsSimulationOnly == true;
        var cloudSource = candidates
            .Where(item => item.ExternalSystemType == DataSourceExternalSystemType.CloudReadOnly)
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        var simulationSource = candidates
            .Where(item => item.ExternalSystemType == DataSourceExternalSystemType.SimulationBusiness)
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        var source = simulationOnly
            ? simulationSource
            : cloudSource is not null && (simulationSource is null || selectedIds.Contains(cloudSource.Id))
                ? cloudSource
                : simulationSource;

        if (source is null)
        {
            throw new InvalidOperationException(simulationOnly
                ? "The confirmed SimulationBusiness source is no longer available; Cloud/Real fallback is forbidden."
                : "No authorized SimulationBusiness or CloudReadOnly data source is available for this agent task.");
        }

        if (source.ExternalSystemType == DataSourceExternalSystemType.CloudReadOnly)
        {
            return await QueryCloudReadOnlyBusinessDatabaseAsync(source, taskGoal, state, cancellationToken);
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

    private async Task<object> QueryCloudReadOnlyBusinessDatabaseAsync(
        BusinessDatabaseDescriptor source,
        string taskGoal,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        if (businessDatabaseReadService is null || cloudTextToSqlFallbackRunner is null)
        {
            throw new InvalidOperationException("CloudReadOnly Text-to-SQL runtime is not configured.");
        }

        var database = await businessDatabaseReadService.GetByNameAsync(source.Name, cancellationToken);
        if (database is null)
        {
            throw new InvalidOperationException("Selected CloudReadOnly data source is not available for this agent task.");
        }

        var fallbackResult = await cloudTextToSqlFallbackRunner.RunAsync(
            database,
            taskGoal,
            source.DefaultQueryLimit,
            cancellationToken);
        if (!fallbackResult.Succeeded)
        {
            throw new InvalidOperationException($"CloudReadOnly Text-to-SQL fallback failed: {fallbackResult.SafeMessage}");
        }

        var rows = fallbackResult.Rows
            .Select(row => row.ToDictionary(item => item.Key, item => item.Value))
            .ToArray();
        var sourceLabel = "Cloud 已有正式只读数据（DataAnalysis/Text-to-SQL 补充分析）";

        state.CloudReadonlySummary =
            $"BusinessDatabase CloudReadOnly Text-to-SQL executed. sourceType=BusinessDatabase; sourceMode=CloudReadOnly; isSimulation=false; sourceLabel={sourceLabel}; queryHash={fallbackResult.QueryHash}; rows={fallbackResult.RowCount}; truncated={fallbackResult.IsTruncated.ToString().ToLowerInvariant()}; repairAttempts={fallbackResult.RepairAttempts.Count}.";
        state.CloudReadonlyRows = rows;
        state.CloudReadonlySourceLabel = sourceLabel;
        state.CloudReadonlySourcePath = "BusinessDataSourceCenter/CloudReadOnlyTextToSql";
        state.CloudReadonlySourceMode = DataSourceExternalSystemType.CloudReadOnly.ToString();
        state.CloudReadonlyIsSimulation = false;
        state.CloudReadonlyRowCount = fallbackResult.RowCount;
        state.CloudReadonlyIsTruncated = fallbackResult.IsTruncated;
        state.BusinessQueryHash = fallbackResult.QueryHash;
        state.BusinessQueryResults.Add(new AgentBusinessQuerySummary(
            source.Id,
            source.Name,
            DataSourceExternalSystemType.CloudReadOnly.ToString(),
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
            sourceMode = DataSourceExternalSystemType.CloudReadOnly.ToString(),
            isSimulation = false,
            rowCount = fallbackResult.RowCount,
            isTruncated = fallbackResult.IsTruncated,
            resultHash = fallbackResult.QueryHash
        };
    }

    public async Task<object> QueryBusinessDatabaseReadonlyAsync(
        AgentTaskPlanDocument plan,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        if (businessDatabaseReadService is null)
        {
            throw new InvalidOperationException("Business database read service is not configured.");
        }

        var enabledSources = await businessDatabaseReadService.ListEnabledAsync(cancellationToken);
        var selectedIds = (plan.DataSourceIds ?? [])
            .Where(id => id != Guid.Empty)
            .ToHashSet();
        var selectedDomains = (plan.BusinessDomains ?? [])
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Select(domain => domain.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = enabledSources
            .Where(source => source.IsSelectableInAgent)
            .Where(source => selectedIds.Count == 0 || selectedIds.Contains(source.Id))
            .Where(source => selectedDomains.Count == 0 ||
                             selectedDomains.Contains(source.BusinessDomain) ||
                             selectedDomains.Contains(source.Category))
            .ToArray();

        if (candidates.Length == 0)
        {
            throw new InvalidOperationException("No authorized BusinessDatabase data source is available for this agent task.");
        }

        var rows = candidates.Select(source => new Dictionary<string, object?>
        {
            ["dataSourceId"] = source.Id,
            ["dataSourceName"] = source.Name,
            ["sourceType"] = "BusinessDatabase",
            ["sourceMode"] = source.ExternalSystemType.ToString(),
            ["isSimulation"] = source.ExternalSystemType == DataSourceExternalSystemType.SimulationBusiness,
            ["sourceLabel"] = source.ExternalSystemType == DataSourceExternalSystemType.SimulationBusiness
                ? "AI 独立模拟业务库"
                : source.Name,
            ["businessDomain"] = source.BusinessDomain,
            ["category"] = source.Category,
            ["sensitivityLevel"] = source.SensitivityLevel,
            ["defaultQueryLimit"] = source.DefaultQueryLimit,
            ["maxQueryLimit"] = source.MaxQueryLimit
        }).ToArray();

        var hasSimulation = candidates.Any(source => source.ExternalSystemType == DataSourceExternalSystemType.SimulationBusiness);
        var queryHash = ComputeHash($"{plan.Goal}|{string.Join(',', candidates.Select(source => source.Id))}|{plan.QueryMode ?? "TextToSql"}");

        state.CloudReadonlySummary =
            $"BusinessDatabase readonly query prepared. sourceType=BusinessDatabase; sourceMode={(hasSimulation ? "SimulationBusiness" : "NonCloud")}; isSimulation={hasSimulation.ToString().ToLowerInvariant()}; sourceLabel={(hasSimulation ? "AI 独立模拟业务库" : string.Join(", ", candidates.Select(source => source.Name)))}; queryHash={queryHash}.";
        state.CloudReadonlyRows = rows;
        state.CloudReadonlySourceLabel = hasSimulation ? "AI 独立模拟业务库" : string.Join(", ", candidates.Select(source => source.Name));
        state.CloudReadonlySourcePath = "BusinessDataSourceCenter";
        state.CloudReadonlySourceMode = hasSimulation ? "SimulationBusiness" : "NonCloud";
        state.CloudReadonlyIsSimulation = hasSimulation;
        state.CloudReadonlyRowCount = rows.Length;
        state.CloudReadonlyIsTruncated = false;
        state.BusinessQueryHash = queryHash;

        return new
        {
            status = "completed",
            resultType = "business-query-summary",
            sourceMode = state.CloudReadonlySourceMode ?? "Unavailable",
            isSimulation = state.CloudReadonlyIsSimulation,
            rowCount = rows.Length,
            isTruncated = false,
            resultHash = queryHash
        };
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
