using System.Security.Cryptography;
using System.Text;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed class AgentRuntimeBusinessQueryToolService(
    IBusinessDatabaseReadService? businessDatabaseReadService,
    IBusinessTextToSqlRuntime? businessTextToSqlRuntime)
{
    public async Task<object> QueryBusinessDatabaseReadonlyP1Async(
        AgentTaskPlanDocument plan,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        if (businessDatabaseReadService is null || businessTextToSqlRuntime is null)
        {
            throw new InvalidOperationException("Business Text-to-SQL runtime is not configured.");
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

        var source = enabledSources
            .Where(item => item.IsSelectableInAgent)
            .Where(item => item.ExternalSystemType == DataSourceExternalSystemType.SimulationBusiness)
            .Where(item => selectedIds.Count == 0 || selectedIds.Contains(item.Id))
            .Where(item => selectedDomains.Count == 0 ||
                           selectedDomains.Contains(item.BusinessDomain) ||
                           selectedDomains.Contains(item.Category))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (source is null)
        {
            throw new InvalidOperationException("No authorized SimulationBusiness data source is available for this agent task.");
        }

        var draftResult = await businessTextToSqlRuntime.GenerateDraftAsync(
            new BusinessTextToSqlDraftRequest(
                source.Id,
                plan.Goal,
                plan.BusinessDomains,
                source.DefaultQueryLimit,
                PreviewOnly: false),
            cancellationToken);
        if (!draftResult.IsSuccess || draftResult.Value is null)
        {
            throw new InvalidOperationException($"Business Text-to-SQL draft failed: {BuildResultErrorSummary(draftResult)}");
        }

        var draft = draftResult.Value;
        var queryResult = await businessTextToSqlRuntime.ExecuteAsync(
            new BusinessTextToSqlExecuteRequest(DraftId: draft.DraftId, RequestedLimit: source.DefaultQueryLimit),
            cancellationToken);
        if (!queryResult.IsSuccess || queryResult.Value is null)
        {
            throw new InvalidOperationException($"Business Text-to-SQL execution failed: {BuildResultErrorSummary(queryResult)}");
        }

        var result = queryResult.Value;
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
            sourceType = result.SourceType,
            sourceMode = result.SourceMode.ToString(),
            isSimulation = result.IsSimulation,
            sourceLabel = result.SourceLabel,
            queryHash = result.QueryHash,
            questionHash = draft.QuestionHash,
            sqlHash = draft.SqlHash,
            rowCount = result.RowCount,
            isTruncated = result.IsTruncated,
            columns = result.Columns,
            rows
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
            sourceType = "BusinessDatabase",
            sourceMode = state.CloudReadonlySourceMode,
            isSimulation = state.CloudReadonlyIsSimulation,
            sourceLabel = state.CloudReadonlySourceLabel,
            queryHash,
            rowCount = rows.Length,
            rows
        };
    }

    public object SummarizeBusinessQueryResult(AgentTaskRunState state)
    {
        return new
        {
            status = "completed",
            sourceType = "BusinessDatabase",
            sourceMode = state.CloudReadonlySourceMode,
            isSimulation = state.CloudReadonlyIsSimulation,
            sourceLabel = state.CloudReadonlySourceLabel,
            queryHash = state.BusinessQueryHash,
            rowCount = state.CloudReadonlyRowCount,
            summary = state.CloudReadonlySummary ?? "BusinessDatabase readonly query result is not available."
        };
    }

    private static string BuildResultErrorSummary(IResult result)
    {
        return result.Errors is null
            ? result.Status.ToString()
            : string.Join("; ", result.Errors.Select(error => error?.ToString()).Where(error => !string.IsNullOrWhiteSpace(error)));
    }

    private static string ComputeHash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
