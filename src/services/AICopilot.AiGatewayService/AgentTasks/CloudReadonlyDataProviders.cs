using System.Globalization;
using System.Text.Json;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Options;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed class CloudReadonlyDataProviderResolver(
    IOptions<CloudReadonlyOptions> options,
    IEnumerable<ICloudReadonlyDataProvider> providers) : ICloudReadonlyDataProviderResolver
{
    public ICloudReadonlyDataProvider Resolve()
    {
        var mode = options.Value.Mode;
        return providers.FirstOrDefault(provider => provider.Mode == mode)
               ?? throw new CloudAiReadException(
                   CloudAiReadProblemCodes.NotConfigured,
                   $"CloudReadonly provider '{mode}' is not registered.");
    }
}

internal sealed class DisabledCloudReadonlyDataProvider : ICloudReadonlyDataProvider
{
    public CloudReadonlyDataSourceMode Mode => CloudReadonlyDataSourceMode.Disabled;

    public Task<CloudReadonlyAgentToolResult> QueryAsync(
        CloudReadonlyAgentToolRequest request,
        CancellationToken cancellationToken = default)
    {
        throw new CloudAiReadException(
            AppProblemCodes.CloudReadonlyToolDisabled,
            "CloudReadonly is disabled. Production deployments must configure CloudReadonly:Mode=Real with Cloud AiRead.");
    }
}

internal sealed class RealCloudReadonlyDataProvider(
    ICloudAiReadClient cloudAiReadClient,
    IOptions<CloudReadonlyOptions> options) : ICloudReadonlyDataProvider
{
    public CloudReadonlyDataSourceMode Mode => CloudReadonlyDataSourceMode.Real;

    public async Task<CloudReadonlyAgentToolResult> QueryAsync(
        CloudReadonlyAgentToolRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!options.Value.Real.Enabled || !options.Value.Real.AllowProductionRead || !cloudAiReadClient.IsEnabled)
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.NotConfigured,
                "CloudReadonly Real mode requires CloudReadonly:Real and CloudAiRead to be explicitly enabled.");
        }

        var semanticPlan = request.SemanticPlan;
        if (!CloudAiReadSemanticSupport.IsSupported(semanticPlan.Target))
        {
            throw new CloudAiReadException(
                AppProblemCodes.CloudReadonlyIntentUnsupported,
                $"Cloud readonly semantic target '{semanticPlan.Target}' is not supported.");
        }

        var queryResult = await cloudAiReadClient.QuerySemanticAsync(semanticPlan, cancellationToken);
        var rows = queryResult.Rows
            .Take(20)
            .Select(row => CloudReadonlyAgentToolResultBuilder.NormalizeRow(
                row,
                CloudReadonlySourceMarkers.RealSourceMode,
                isSimulation: false,
                queryResult.SourceLabel))
            .ToArray();
        var summary = CloudReadonlyAgentToolResultBuilder.BuildSummary(
            semanticPlan.Intent,
            semanticPlan.Target.ToString(),
            semanticPlan.Kind.ToString(),
            queryResult.SourcePath,
            queryResult.SourceLabel,
            CloudReadonlySourceMarkers.RealSourceMode,
            false,
            queryResult.IsTruncated,
            queryResult.Rows.Count,
            rows);

        return new CloudReadonlyAgentToolResult(
            "completed",
            semanticPlan.Intent,
            semanticPlan.Target.ToString(),
            semanticPlan.Kind.ToString(),
            queryResult.SourcePath,
            queryResult.SourceLabel,
            CloudReadonlySourceMarkers.RealSourceMode,
            false,
            queryResult.QueriedAtUtc,
            queryResult.Limit,
            queryResult.IsTruncated,
            queryResult.Rows.Count,
            rows,
            summary);
    }
}

internal static class CloudReadonlyAgentToolGuards
{
    public static void ValidateRequest(CloudReadonlyAgentToolRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Intent))
        {
            throw new CloudAiReadException(
                AppProblemCodes.CloudReadonlyIntentUnsupported,
                "Cloud readonly intent is missing from the agent plan.");
        }

        var plan = request.SemanticPlan;
        if (CloudReadonlyAgentTextGuard.ContainsForbiddenWriteSemantic(request.Intent) ||
            !string.IsNullOrWhiteSpace(plan.QueryText) ||
            !Enum.IsDefined(plan.Target) ||
            !Enum.IsDefined(plan.Kind) ||
            plan.Target == SemanticQueryTarget.Recipe ||
            plan.Projection is null ||
            plan.Projection.Fields is null ||
            plan.Filters is null ||
            plan.Filters.Any(filter =>
                filter is null ||
                string.IsNullOrWhiteSpace(filter.Field) ||
                string.IsNullOrWhiteSpace(filter.Value) ||
                !Enum.IsDefined(filter.Operator)) ||
            !CloudAiReadRowLimitPolicy.IsWithinBounds(plan.Limit) ||
            !string.Equals(
                request.SemanticPlanDigest,
                AgentTaskPlanCloudReadonlyIntentDocument.ComputeSemanticPlanDigest(plan),
                StringComparison.Ordinal))
        {
            throw new CloudAiReadException(
                AppProblemCodes.CloudReadonlyIntentUnsupported,
                "Cloud readonly intent violates the frozen typed semantic plan contract.");
        }
    }
}

internal static class CloudReadonlyAgentToolResultBuilder
{
    public static Dictionary<string, object?> NormalizeRow(
        IReadOnlyDictionary<string, object?> row,
        string sourceMode,
        bool isSimulation,
        string sourceLabel)
    {
        var normalized = row.ToDictionary(
            item => item.Key,
            item => NormalizeValue(item.Value),
            StringComparer.OrdinalIgnoreCase);
        normalized["sourceMode"] = sourceMode;
        normalized["isSimulation"] = isSimulation;
        normalized["sourceLabel"] = sourceLabel;
        return normalized;
    }

    public static string BuildSummary(
        string intent,
        string target,
        string kind,
        string sourcePath,
        string sourceLabel,
        string sourceMode,
        bool isSimulation,
        bool isTruncated,
        int rowCount,
        IReadOnlyCollection<Dictionary<string, object?>> rows)
    {
        var sample = rows.Count == 0
            ? "no rows"
            : string.Join(
                " | ",
                rows.Take(3).Select(row =>
                    string.Join(
                        ", ",
                        row.Take(6).Select(item => $"{item.Key}={FormatSummaryValue(item.Value)}"))));
        var summary =
            $"CloudReadonly returned {rowCount} row(s) for {target}/{kind}; " +
            $"intent={intent}; sourcePath={sourcePath}; sourceLabel={sourceLabel}; " +
            $"sourceMode={sourceMode}; isSimulation={isSimulation}; truncated={isTruncated}; sample={sample}";
        return CloudReadonlyAgentTextGuard.SanitizeForPlan(summary, 2000) ?? "CloudReadonly completed.";
    }

    public static int ResolveLimit(CloudReadonlySimulationQuery query, int defaultLimit)
    {
        return CloudAiReadRowLimitPolicy.Normalize(
            query.Limit is > 0 ? query.Limit.Value : defaultLimit);
    }

    private static object? NormalizeValue(object? value)
    {
        return value switch
        {
            null => null,
            string text => CloudReadonlyAgentTextGuard.SanitizeForPlan(text, 300),
            bool => value,
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => value,
            DateTime => value,
            DateTimeOffset => value,
            JsonElement element => NormalizeJsonElement(element),
            _ => CloudReadonlyAgentTextGuard.SanitizeForPlan(
                Convert.ToString(value, CultureInfo.InvariantCulture),
                300)
        };
    }

    private static object? NormalizeJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when element.TryGetDecimal(out var decimalValue) => decimalValue,
            JsonValueKind.String when element.TryGetDateTimeOffset(out var dateValue) => dateValue,
            JsonValueKind.String => CloudReadonlyAgentTextGuard.SanitizeForPlan(element.GetString(), 300),
            _ => CloudReadonlyAgentTextGuard.SanitizeForPlan(element.GetRawText(), 300)
        };
    }

    private static string FormatSummaryValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            DateTimeOffset date => date.ToString("O", CultureInfo.InvariantCulture),
            DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }
}
