using System.Text.RegularExpressions;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Workflows.Executors;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Options;

namespace AICopilot.AiGatewayService.AgentTasks;

public sealed record CloudReadonlyAgentPlanIntent(
    SemanticQueryPlan SemanticPlan,
    double Confidence,
    string SemanticPlanDigest)
{
    public string Intent => SemanticPlan.Intent;

    public static CloudReadonlyAgentPlanIntent FromSemanticPlan(
        SemanticQueryPlan semanticPlan,
        double confidence)
    {
        ArgumentNullException.ThrowIfNull(semanticPlan);
        return new CloudReadonlyAgentPlanIntent(
            semanticPlan,
            confidence,
            AgentTaskPlanCloudReadonlyIntentDocument.ComputeSemanticPlanDigest(semanticPlan));
    }
}

public interface ICloudReadonlyAgentPlanService
{
    Result<CloudReadonlyAgentPlanIntent> CreateIntentFromRouted(
        string goal,
        IReadOnlyCollection<IntentResult> routedIntents);

    Result<IReadOnlyCollection<CloudReadonlyAgentPlanIntent>> CreateIntentsFromRouted(
        string goal,
        IReadOnlyCollection<IntentResult> routedIntents);
}

public sealed class CloudReadonlyAgentPlanService(
    ISemanticQueryPlanner semanticQueryPlanner) : ICloudReadonlyAgentPlanService
{
    private const double MinimumConfidence = 0.65;

    public Result<CloudReadonlyAgentPlanIntent> CreateIntentFromRouted(
        string goal,
        IReadOnlyCollection<IntentResult> routedIntents)
    {
        var result = CreateIntentsFromRouted(goal, routedIntents);
        if (!result.IsSuccess)
        {
            return Result.From(result);
        }

        return Result.Success(result.Value!
            .OrderByDescending(intent => intent.Confidence)
            .ThenBy(intent => intent.Intent, StringComparer.Ordinal)
            .First());
    }

    public Result<IReadOnlyCollection<CloudReadonlyAgentPlanIntent>> CreateIntentsFromRouted(
        string goal,
        IReadOnlyCollection<IntentResult> routedIntents)
    {
        var candidates = routedIntents
            .Where(IsSupportedIntentFamily)
            .GroupBy(intent => intent.Intent, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(intent => intent.Confidence)
                .ThenBy(intent => intent.Query ?? string.Empty, StringComparer.Ordinal)
                .First())
            .OrderBy(intent => intent.Intent, StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length == 0)
        {
            return UnsupportedMany("Cloud readonly agent plans only support device, device log, capacity, production data, process, and client release intents.");
        }

        var planned = new List<CloudReadonlyAgentPlanIntent>(candidates.Length);
        foreach (var candidate in candidates)
        {
            var intent = CreateIntentFromCandidate(candidate);
            if (!intent.IsSuccess)
            {
                return Result.From(intent);
            }

            planned.Add(intent.Value!);
        }

        return Result.Success<IReadOnlyCollection<CloudReadonlyAgentPlanIntent>>(planned);
    }

    private Result<CloudReadonlyAgentPlanIntent> CreateIntentFromCandidate(IntentResult candidate)
    {
        if (candidate.Confidence < MinimumConfidence)
        {
            return Unsupported("Cloud readonly agent intent confidence is below the execution threshold.");
        }

        var planningResult = semanticQueryPlanner.Plan(candidate.Intent, candidate.Query);
        if (!planningResult.IsSuccess || planningResult.Plan is null)
        {
            return Unsupported(planningResult.ErrorMessage ?? "Cloud readonly semantic plan could not be built.");
        }

        var semanticPlan = planningResult.Plan;
        if (!CloudAiReadSemanticSupport.IsSupported(semanticPlan.Target))
        {
            return Unsupported($"Cloud readonly semantic target '{semanticPlan.Target}' is not supported.");
        }

        var normalizedFilters = new List<SemanticFilter>(semanticPlan.Filters.Count);
        foreach (var filter in semanticPlan.Filters)
        {
            var operatorCode = filter.Operator switch
            {
                SemanticFilterOperator.Contains => "contains",
                SemanticFilterOperator.GreaterOrEqual => "gte",
                SemanticFilterOperator.LessOrEqual => "lte",
                SemanticFilterOperator.In => "in",
                _ => "eq"
            };
            if (!CloudAiReadSemanticSchemaRegistry.TryNormalizeFilter(
                    semanticPlan.Intent,
                    filter.Field,
                    operatorCode,
                    filter.Value,
                    out var normalized))
            {
                return Unsupported(
                    $"Cloud readonly intent '{semanticPlan.Intent}' contains a filter outside the production provider schema.");
            }

            normalizedFilters.Add(new SemanticFilter(
                normalized.Field,
                normalized.Operator switch
                {
                    "contains" => SemanticFilterOperator.Contains,
                    "gte" => SemanticFilterOperator.GreaterOrEqual,
                    "lte" => SemanticFilterOperator.LessOrEqual,
                    "in" => SemanticFilterOperator.In,
                    _ => SemanticFilterOperator.Equal
                },
                normalized.Value));
        }

        if (!CloudAiReadSemanticSchemaRegistry.MatchesIntentScope(
                semanticPlan.Intent,
                normalizedFilters.Select(filter => new CloudAiReadFilter(
                    filter.Field,
                    filter.Operator switch
                    {
                        SemanticFilterOperator.Contains => "contains",
                        SemanticFilterOperator.GreaterOrEqual => "gte",
                        SemanticFilterOperator.LessOrEqual => "lte",
                        SemanticFilterOperator.In => "in",
                        _ => "eq"
                    },
                    filter.Value)).ToArray(),
                semanticPlan.TimeRange is not null))
        {
            return Unsupported(
                $"Cloud readonly intent '{semanticPlan.Intent}' does not match the production provider scope requirements.");
        }

        return Result.Success(CloudReadonlyAgentPlanIntent.FromSemanticPlan(
            semanticPlan with
            {
                QueryText = null,
                Filters = normalizedFilters
                    .OrderBy(filter => filter.Field, StringComparer.Ordinal)
                    .ThenBy(filter => filter.Operator)
                    .ThenBy(filter => filter.Value, StringComparer.Ordinal)
                    .ToArray()
            },
            candidate.Confidence));
    }

    private static bool IsSupportedIntentFamily(IntentResult intent)
    {
        return intent.Intent.StartsWith("Analysis.Device.", StringComparison.OrdinalIgnoreCase) ||
               intent.Intent.StartsWith("Analysis.DeviceLog.", StringComparison.OrdinalIgnoreCase) ||
               intent.Intent.StartsWith("Analysis.Capacity.", StringComparison.OrdinalIgnoreCase) ||
               intent.Intent.StartsWith("Analysis.ProductionData.", StringComparison.OrdinalIgnoreCase) ||
               intent.Intent.StartsWith("Analysis.Process.", StringComparison.OrdinalIgnoreCase) ||
               intent.Intent.StartsWith("Analysis.ClientRelease.", StringComparison.OrdinalIgnoreCase);
    }

    private static Result<CloudReadonlyAgentPlanIntent> Unsupported(string detail)
    {
        return Result.Failure(new ApiProblemDescriptor(
            AppProblemCodes.CloudReadonlyIntentUnsupported,
            detail));
    }

    private static Result<IReadOnlyCollection<CloudReadonlyAgentPlanIntent>> UnsupportedMany(string detail)
    {
        return Result.Failure(new ApiProblemDescriptor(
            AppProblemCodes.CloudReadonlyIntentUnsupported,
            detail));
    }
}

internal static class CloudReadonlyAgentTextGuard
{
    public static bool ContainsUnsafePersistedPayload(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Regex.IsMatch(value, @"(?i)(api[_-]?key|token|password|secret|connection\s*string)\s*[""':=]") ||
               Regex.IsMatch(value, @"[A-Za-z]:\\[^\s""']+");
    }

    public static string? SanitizeForPlan(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var sanitized = Regex.Replace(
            value,
            @"(?i)(api[_-]?key|token|password|secret|connection\s*string)\s*[""':=]+\s*[^,""}\s]+",
            "$1=******");
        sanitized = Regex.Replace(sanitized, @"[A-Za-z]:\\[^\s""']+", "[redacted-path]");
        sanitized = Regex.Replace(
            sanitized,
            @"(?i)(Host|Username|Password|Database|Port)\s*=\s*[^;""'}]+",
            "$1=******");
        sanitized = sanitized.Trim();
        return sanitized.Length <= maxLength ? sanitized : sanitized[..maxLength];
    }
}
