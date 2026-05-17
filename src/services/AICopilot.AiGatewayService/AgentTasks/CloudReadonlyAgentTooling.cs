using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Workflows.Executors;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

public sealed record CloudReadonlyAgentPlanIntent(
    string Intent,
    string? Query,
    double Confidence,
    string Target,
    string Kind,
    string Summary);

public interface ICloudReadonlyAgentPlanService
{
    Task<Result<CloudReadonlyAgentPlanIntent>> CreateIntentAsync(
        Guid sessionId,
        string goal,
        CancellationToken cancellationToken = default);
}

public interface ICloudReadonlyAgentIntentRouter
{
    Task<IReadOnlyCollection<IntentResult>> RouteAsync(
        Guid sessionId,
        string goal,
        CancellationToken cancellationToken = default);
}

public sealed class CloudReadonlyAgentIntentRouter(IntentRoutingExecutor intentRoutingExecutor)
    : ICloudReadonlyAgentIntentRouter
{
    public async Task<IReadOnlyCollection<IntentResult>> RouteAsync(
        Guid sessionId,
        string goal,
        CancellationToken cancellationToken = default)
    {
        var result = await intentRoutingExecutor.ExecuteAsync(
            new ChatStreamRequest(sessionId, goal),
            cancellationToken);
        return result.Intents;
    }
}

public sealed class CloudReadonlyAgentPlanService(
    ICloudReadonlyAgentIntentRouter intentRouter,
    ISemanticQueryPlanner semanticQueryPlanner) : ICloudReadonlyAgentPlanService
{
    private const double MinimumConfidence = 0.65;

    public async Task<Result<CloudReadonlyAgentPlanIntent>> CreateIntentAsync(
        Guid sessionId,
        string goal,
        CancellationToken cancellationToken = default)
    {
        if (CloudReadonlyAgentTextGuard.ContainsForbiddenWriteSemantic(goal))
        {
            return Unsupported("Cloud readonly agent plans cannot contain write semantics.");
        }

        var routedIntents = await intentRouter.RouteAsync(sessionId, goal, cancellationToken);
        var candidate = routedIntents
            .Where(IsSupportedIntentFamily)
            .OrderByDescending(intent => intent.Confidence)
            .FirstOrDefault();
        if (candidate is null)
        {
            return Unsupported("Cloud readonly agent plans only support device, device log, capacity, and production data intents.");
        }

        if (candidate.Confidence < MinimumConfidence)
        {
            return Unsupported("Cloud readonly agent intent confidence is below the execution threshold.");
        }

        if (CloudReadonlyAgentTextGuard.ContainsForbiddenWriteSemantic(candidate.Intent) ||
            CloudReadonlyAgentTextGuard.ContainsForbiddenWriteSemantic(candidate.Query) ||
            CloudReadonlyAgentTextGuard.ContainsUnsafePersistedPayload(candidate.Query))
        {
            return Unsupported("Cloud readonly agent intent contains unsafe query semantics.");
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

        var summary = $"target={semanticPlan.Target}; kind={semanticPlan.Kind}; filters={semanticPlan.Filters.Count}; hasTimeRange={semanticPlan.TimeRange is not null}; limit={semanticPlan.Limit}";
        return Result.Success(new CloudReadonlyAgentPlanIntent(
            semanticPlan.Intent,
            CloudReadonlyAgentTextGuard.SanitizeForPlan(candidate.Query, 2000),
            candidate.Confidence,
            semanticPlan.Target.ToString(),
            semanticPlan.Kind.ToString(),
            summary));
    }

    private static bool IsSupportedIntentFamily(IntentResult intent)
    {
        return intent.Intent.StartsWith("Analysis.Device.", StringComparison.OrdinalIgnoreCase) ||
               intent.Intent.StartsWith("Analysis.DeviceLog.", StringComparison.OrdinalIgnoreCase) ||
               intent.Intent.StartsWith("Analysis.Capacity.", StringComparison.OrdinalIgnoreCase) ||
               intent.Intent.StartsWith("Analysis.ProductionData.", StringComparison.OrdinalIgnoreCase);
    }

    private static Result<CloudReadonlyAgentPlanIntent> Unsupported(string detail)
    {
        return Result.Failure(new ApiProblemDescriptor(
            AppProblemCodes.CloudReadonlyIntentUnsupported,
            detail));
    }
}

public sealed record CloudReadonlyAgentToolRequest(
    string Intent,
    string? Query,
    double Confidence);

public sealed record CloudReadonlyAgentToolResult(
    string Status,
    string Intent,
    string Target,
    string Kind,
    string SourcePath,
    string SourceLabel,
    DateTimeOffset QueriedAtUtc,
    int Limit,
    bool IsTruncated,
    int RowCount,
    IReadOnlyList<Dictionary<string, object?>> Rows,
    string Summary);

public interface ICloudReadonlyAgentToolExecutor
{
    Task<CloudReadonlyAgentToolResult> ExecuteAsync(
        CloudReadonlyAgentToolRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class CloudReadonlyAgentToolExecutor(
    ISemanticQueryPlanner semanticQueryPlanner,
    ICloudAiReadClient cloudAiReadClient) : ICloudReadonlyAgentToolExecutor
{
    public async Task<CloudReadonlyAgentToolResult> ExecuteAsync(
        CloudReadonlyAgentToolRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Intent))
        {
            throw new CloudAiReadException(
                AppProblemCodes.CloudReadonlyIntentUnsupported,
                "Cloud readonly intent is missing from the agent plan.");
        }

        if (CloudReadonlyAgentTextGuard.ContainsForbiddenWriteSemantic(request.Intent) ||
            CloudReadonlyAgentTextGuard.ContainsForbiddenWriteSemantic(request.Query) ||
            CloudReadonlyAgentTextGuard.ContainsUnsafePersistedPayload(request.Query))
        {
            throw new CloudAiReadException(
                AppProblemCodes.CloudReadonlyIntentUnsupported,
                "Cloud readonly intent contains unsafe query semantics.");
        }

        var planningResult = semanticQueryPlanner.Plan(request.Intent, request.Query);
        if (!planningResult.IsSuccess || planningResult.Plan is null)
        {
            throw new CloudAiReadException(
                AppProblemCodes.CloudReadonlyIntentUnsupported,
                planningResult.ErrorMessage ?? "Cloud readonly semantic plan could not be built.");
        }

        var semanticPlan = planningResult.Plan;
        if (!CloudAiReadSemanticSupport.IsSupported(semanticPlan.Target))
        {
            throw new CloudAiReadException(
                AppProblemCodes.CloudReadonlyIntentUnsupported,
                $"Cloud readonly semantic target '{semanticPlan.Target}' is not supported.");
        }

        var queryResult = await cloudAiReadClient.QuerySemanticAsync(semanticPlan, cancellationToken);
        var rows = queryResult.Rows
            .Take(20)
            .Select(NormalizeRow)
            .ToArray();
        var summary = BuildSummary(semanticPlan, queryResult, rows);

        return new CloudReadonlyAgentToolResult(
            "completed",
            semanticPlan.Intent,
            semanticPlan.Target.ToString(),
            semanticPlan.Kind.ToString(),
            queryResult.SourcePath,
            queryResult.SourceLabel,
            queryResult.QueriedAtUtc,
            queryResult.Limit,
            queryResult.IsTruncated,
            queryResult.Rows.Count,
            rows,
            summary);
    }

    private static Dictionary<string, object?> NormalizeRow(Dictionary<string, object?> row)
    {
        return row.ToDictionary(
            item => item.Key,
            item => NormalizeValue(item.Value),
            StringComparer.OrdinalIgnoreCase);
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

    private static string BuildSummary(
        SemanticQueryPlan semanticPlan,
        CloudAiReadResult<object> queryResult,
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
            $"Cloud AiRead returned {queryResult.Rows.Count} row(s) for {semanticPlan.Target}/{semanticPlan.Kind}; " +
            $"sourcePath={queryResult.SourcePath}; sourceLabel={queryResult.SourceLabel}; truncated={queryResult.IsTruncated}; sample={sample}";
        return CloudReadonlyAgentTextGuard.SanitizeForPlan(summary, 2000) ?? "Cloud AiRead completed.";
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

internal static class CloudReadonlyAgentTextGuard
{
    private static readonly string[] ForbiddenWriteTokens =
    [
        "create",
        "update",
        "delete",
        "register",
        "disable",
        "approve",
        "dispatch",
        "trigger",
        "backfill",
        "correct",
        "upload",
        "submit",
        "write",
        "修改",
        "删除",
        "新增",
        "禁用",
        "审批",
        "派发",
        "触发",
        "补录",
        "回填",
        "上传",
        "写入"
    ];

    public static bool ContainsForbiddenWriteSemantic(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return ForbiddenWriteTokens.Any(token =>
            token.Any(ch => ch > 127)
                ? value.Contains(token, StringComparison.OrdinalIgnoreCase)
                : Regex.IsMatch(value, $@"(?i)(^|[^\p{{L}}\p{{N}}_]){Regex.Escape(token)}([^\p{{L}}\p{{N}}_]|$)"));
    }

    public static bool ContainsUnsafePersistedPayload(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Regex.IsMatch(value, @"(?i)(api[_-]?key|token|password|secret|connection\s*string)\s*[""':=]") ||
               Regex.IsMatch(value, @"[A-Za-z]:\\[^\s""']+") ||
               Regex.IsMatch(value, @"(?is)\b(select|insert|update|delete|drop|alter|truncate|merge)\b.+\b(from|into|table|set)\b");
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
