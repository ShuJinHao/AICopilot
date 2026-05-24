using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;
using MediatR;
using Microsoft.Extensions.Options;

namespace AICopilot.AiGatewayService.CloudReadiness;

public static class CloudReadonlySandboxControlledTrialStatuses
{
    public const string Disabled = "Disabled";
    public const string Ready = "Ready";
    public const string FreeGoalDisabled = "FreeGoalDisabled";
    public const string SandboxSmokeRequired = "SandboxSmokeRequired";
    public const string FixedTrialRequired = "FixedTrialRequired";
    public const string Blocked = "Blocked";
    public const string Failed = "Failed";
    public const string Completed = "Completed";
}

public sealed record CloudReadonlySandboxControlledTrialStatusDto(
    string Status,
    string SandboxSmokeStatus,
    string FixedTrialStatus,
    bool ControlledTrialEnabled,
    bool FreeGoalEnabled,
    bool ToolVisible,
    bool ToolExecutable,
    DateTimeOffset? LastTrialAt,
    IReadOnlyCollection<string> Errors,
    IReadOnlyCollection<string> Warnings,
    string Boundary = CloudReadonlySandboxControlledTrialMarkers.Boundary);

public sealed record CloudSandboxGoalTimeRangeDto(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null);

public sealed record CloudSandboxGoalIntentDto(
    string IntentId,
    string GoalHash,
    IReadOnlyCollection<string> EndpointCodes,
    CloudSandboxGoalTimeRangeDto TimeRange,
    int MaxRows,
    IReadOnlyCollection<string> ArtifactTypes,
    string AnalysisType,
    IReadOnlyCollection<string> Warnings,
    IReadOnlyCollection<string> RejectedReasons,
    bool RequiresToolApproval,
    bool RequiresFinalApproval);

public sealed record CloudReadonlySandboxControlledPlanDto(
    AgentTaskDto Task,
    CloudSandboxGoalIntentDto Intent);

[AuthorizeRequirement("AiGateway.ToolRegistry.Read")]
public sealed record GetCloudReadonlySandboxControlledTrialStatusQuery
    : IQuery<Result<CloudReadonlySandboxControlledTrialStatusDto>>;

[AuthorizeRequirement("AiGateway.PlanAgentTask")]
public sealed record CreateCloudReadonlySandboxControlledPlanCommand(
    Guid SessionId,
    string Goal,
    Guid? ModelId = null,
    IReadOnlyCollection<string>? ArtifactTypes = null,
    CloudSandboxGoalTimeRangeDto? TimeRange = null,
    int? MaxRows = null,
    string? PlannerMode = null) : ICommand<Result<CloudReadonlySandboxControlledPlanDto>>;

public sealed class GetCloudReadonlySandboxControlledTrialStatusQueryHandler(
    CloudReadonlySandboxControlledTrialService controlledTrialService)
    : IQueryHandler<GetCloudReadonlySandboxControlledTrialStatusQuery, Result<CloudReadonlySandboxControlledTrialStatusDto>>
{
    public Task<Result<CloudReadonlySandboxControlledTrialStatusDto>> Handle(
        GetCloudReadonlySandboxControlledTrialStatusQuery request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success(controlledTrialService.BuildStatus()));
    }
}

public sealed class CreateCloudReadonlySandboxControlledPlanCommandHandler(
    CloudReadonlySandboxControlledTrialService controlledTrialService,
    ISender sender)
    : ICommandHandler<CreateCloudReadonlySandboxControlledPlanCommand, Result<CloudReadonlySandboxControlledPlanDto>>
{
    public async Task<Result<CloudReadonlySandboxControlledPlanDto>> Handle(
        CreateCloudReadonlySandboxControlledPlanCommand request,
        CancellationToken cancellationToken)
    {
        var intentResult = controlledTrialService.CreateIntent(
            request.Goal,
            request.ArtifactTypes,
            request.TimeRange,
            request.MaxRows);
        if (!intentResult.IsSuccess || intentResult.Value is null)
        {
            return Result.From(intentResult);
        }

        var taskResult = await sender.Send(
            new PlanAgentTaskCommand(
                request.SessionId,
                request.Goal,
                AgentTaskType.CloudDataReport,
                request.ModelId,
                ArtifactTypes: intentResult.Value.ArtifactTypes,
                BusinessDomains: intentResult.Value.EndpointCodes,
                QueryMode: CloudReadonlySandboxAgentTrialMarkers.SourceMode,
                RequiresDataApproval: true,
                PlannerMode: request.PlannerMode ?? "Auto",
                IsCloudSandboxControlledTrial: true,
                CloudSandboxGoalIntent: intentResult.Value),
            cancellationToken);
        if (!taskResult.IsSuccess || taskResult.Value is null)
        {
            return Result.From(taskResult);
        }

        return Result.Success(new CloudReadonlySandboxControlledPlanDto(taskResult.Value, intentResult.Value));
    }
}

public interface ICloudReadonlySandboxControlledTrialIntentStore
{
    void Save(CloudSandboxGoalIntentDto intent);

    CloudSandboxGoalIntentDto? Get(string intentId);
}

internal sealed class InMemoryCloudReadonlySandboxControlledTrialIntentStore
    : ICloudReadonlySandboxControlledTrialIntentStore
{
    private readonly object sync = new();
    private readonly Dictionary<string, CloudSandboxGoalIntentDto> items = new(StringComparer.OrdinalIgnoreCase);

    public void Save(CloudSandboxGoalIntentDto intent)
    {
        lock (sync)
        {
            items[intent.IntentId] = intent;
            if (items.Count <= 100)
            {
                return;
            }

            foreach (var key in items.Keys.Take(items.Count - 100).ToArray())
            {
                items.Remove(key);
            }
        }
    }

    public CloudSandboxGoalIntentDto? Get(string intentId)
    {
        lock (sync)
        {
            return items.GetValueOrDefault(intentId);
        }
    }
}

public sealed class CloudReadonlySandboxControlledTrialService(
    IOptions<CloudReadonlyOptions> cloudReadonlyOptions,
    IOptions<CloudReadonlySandboxOptions> cloudReadonlySandboxOptions,
    IOptions<CloudAiReadOptions> cloudAiReadOptions,
    IOptions<CloudReadonlySandboxControlledTrialOptions> controlledOptions,
    ICloudReadonlyReadinessHistoryStore readinessHistoryStore,
    ICloudReadonlySandboxAgentTrialHistoryStore trialHistoryStore,
    ICloudReadonlySandboxControlledTrialIntentStore intentStore,
    ICloudReadonlySandboxClient cloudReadonlySandboxClient,
    CloudReadonlySandboxAgentTrialService fixedTrialService)
{
    private static readonly IReadOnlyDictionary<string, EndpointSpec> EndpointSpecs =
        new[]
        {
            new EndpointSpec("devices", HttpMethod.Get, "/api/v1/ai/read/devices"),
            new EndpointSpec("capacity_summary", HttpMethod.Get, "/api/v1/ai/read/capacity/summary"),
            new EndpointSpec("device_logs", HttpMethod.Get, "/api/v1/ai/read/device-logs"),
            new EndpointSpec("pass_station_records", HttpMethod.Get, "/api/v1/ai/read/pass-stations/default"),
            new EndpointSpec("recipe", HttpMethod.Get, "/api/v1/ai/read/recipes", IsBlockedByPolicy: true),
            new EndpointSpec("recipe_versions", HttpMethod.Get, "/api/v1/ai/read/recipes/versions", IsBlockedByPolicy: true),
            new EndpointSpec("write_path", HttpMethod.Post, "/api/v1/ai/read/devices/update", IsBlockedByPolicy: true)
        }.ToDictionary(item => item.Code, StringComparer.OrdinalIgnoreCase);

    public CloudReadonlySandboxControlledTrialStatusDto BuildStatus()
    {
        var options = controlledOptions.Value;
        var sandbox = cloudReadonlySandboxOptions.Value;
        var fixedStatus = fixedTrialService.BuildStatus();
        var latestSmoke = readinessHistoryStore.List()
            .FirstOrDefault(item => item.Mode == CloudReadonlyReadinessModes.RealSandboxSmoke);
        var latestTrial = trialHistoryStore.List().FirstOrDefault(item =>
            string.Equals(item.Boundary, CloudReadonlySandboxControlledTrialMarkers.Boundary, StringComparison.Ordinal));
        var errors = new List<string>();
        var warnings = new List<string>();

        ValidateProductionBoundary(errors);

        var sandboxSmokeStatus = latestSmoke?.Status ?? CloudReadonlyReadinessStatuses.NotConfigured;
        var smokePassed = string.Equals(sandboxSmokeStatus, CloudReadonlyReadinessStatuses.RealSandboxPassed, StringComparison.Ordinal);
        if (!sandbox.IsConfigured())
        {
            warnings.Add("CloudReadonlySandbox is not configured; controlled sandbox trial requires sandbox smoke configuration.");
        }

        if (!smokePassed)
        {
            warnings.Add("CloudReadonlySandbox RealSandboxSmoke has not passed in this process.");
        }

        if (!options.Enabled)
        {
            return new CloudReadonlySandboxControlledTrialStatusDto(
                CloudReadonlySandboxControlledTrialStatuses.Disabled,
                sandboxSmokeStatus,
                fixedStatus.Status,
                ControlledTrialEnabled: false,
                FreeGoalEnabled: options.FreeGoalEnabled,
                ToolVisible: false,
                ToolExecutable: false,
                LastTrialAt: latestTrial?.QueryResult.ExecutedAt,
                Errors: errors,
                Warnings: warnings);
        }

        if (!options.FreeGoalEnabled)
        {
            return new CloudReadonlySandboxControlledTrialStatusDto(
                CloudReadonlySandboxControlledTrialStatuses.FreeGoalDisabled,
                sandboxSmokeStatus,
                fixedStatus.Status,
                ControlledTrialEnabled: true,
                FreeGoalEnabled: false,
                ToolVisible: false,
                ToolExecutable: false,
                LastTrialAt: latestTrial?.QueryResult.ExecutedAt,
                Errors: errors,
                Warnings: warnings);
        }

        var allowedEndpoints = FilterAllowedEndpointCodes(options.AllowedEndpointCodes);
        if (allowedEndpoints.Length == 0)
        {
            errors.Add("CloudReadonlySandboxControlledTrial has no allowed endpoint codes.");
        }

        var status = errors.Count > 0
            ? CloudReadonlySandboxControlledTrialStatuses.Blocked
            : !smokePassed || !sandbox.IsConfigured()
                ? CloudReadonlySandboxControlledTrialStatuses.SandboxSmokeRequired
                : fixedStatus.Status != CloudReadonlySandboxAgentTrialStatuses.Ready
                    ? CloudReadonlySandboxControlledTrialStatuses.FixedTrialRequired
                    : CloudReadonlySandboxControlledTrialStatuses.Ready;

        return new CloudReadonlySandboxControlledTrialStatusDto(
            status,
            sandboxSmokeStatus,
            fixedStatus.Status,
            ControlledTrialEnabled: options.Enabled,
            FreeGoalEnabled: options.FreeGoalEnabled,
            ToolVisible: status == CloudReadonlySandboxControlledTrialStatuses.Ready,
            ToolExecutable: status == CloudReadonlySandboxControlledTrialStatuses.Ready,
            LastTrialAt: latestTrial?.QueryResult.ExecutedAt,
            Errors: errors,
            Warnings: warnings);
    }

    public Result<CloudSandboxGoalIntentDto> CreateIntent(
        string goal,
        IReadOnlyCollection<string>? artifactTypes,
        CloudSandboxGoalTimeRangeDto? timeRange,
        int? maxRows)
    {
        var status = BuildStatus();
        if (status.Status != CloudReadonlySandboxControlledTrialStatuses.Ready)
        {
            return Result.Invalid($"CloudReadonlySandboxControlledTrial is not ready. Status={status.Status}; Smoke={status.SandboxSmokeStatus}; FixedTrial={status.FixedTrialStatus}.");
        }

        var options = controlledOptions.Value;
        var warnings = new List<string>();
        var rejected = new List<string>();
        var normalizedGoal = NormalizeGoal(goal);
        if (string.IsNullOrWhiteSpace(normalizedGoal))
        {
            rejected.Add("Goal is required.");
        }

        if (ContainsBlockedGoalTerm(normalizedGoal))
        {
            rejected.Add("BlockedByPolicy: controlled sandbox goal cannot request Recipe, write paths, production paths, or Cloud write semantics.");
        }

        var endpointCode = ResolveEndpointCode(normalizedGoal);
        var allowedEndpoints = FilterAllowedEndpointCodes(options.AllowedEndpointCodes);
        if (endpointCode is null)
        {
            rejected.Add("BlockedByPolicy: controlled sandbox goal could not be mapped to an allowed endpoint.");
        }
        else if (!allowedEndpoints.Contains(endpointCode, StringComparer.OrdinalIgnoreCase))
        {
            rejected.Add($"BlockedByPolicy: endpoint '{endpointCode}' is not in CloudReadonlySandboxControlledTrial allowlist.");
        }

        var normalizedArtifactTypes = NormalizeArtifactTypes(artifactTypes, options, rejected);
        var normalizedTimeRange = NormalizeTimeRange(timeRange, options, rejected, warnings);
        var effectiveMaxRows = maxRows ?? options.DefaultMaxRows;
        if (effectiveMaxRows < 1 || effectiveMaxRows > options.MaxRows)
        {
            rejected.Add($"maxRows must be between 1 and {options.MaxRows}.");
        }

        var intent = new CloudSandboxGoalIntentDto(
            $"csg_{ComputeHash($"{normalizedGoal}|{DateTimeOffset.UtcNow:O}")[..20]}",
            ComputeHash(normalizedGoal),
            endpointCode is null ? [] : [endpointCode],
            normalizedTimeRange,
            effectiveMaxRows,
            normalizedArtifactTypes,
            ResolveAnalysisType(normalizedGoal, endpointCode),
            warnings,
            rejected,
            options.RequiresToolApproval,
            options.RequiresFinalApproval);

        if (rejected.Count > 0)
        {
            return Result.Invalid(string.Join("; ", rejected));
        }

        intentStore.Save(intent);
        return Result.Success(intent);
    }

    public Result ValidateIntentForPlan(CloudSandboxGoalIntentDto? intent)
    {
        return ValidateIntent(intent, requireIntentStoreEntry: true);
    }

    private Result ValidateIntentForExecution(CloudSandboxGoalIntentDto? intent)
    {
        return ValidateIntent(intent, requireIntentStoreEntry: false);
    }

    private Result ValidateIntent(CloudSandboxGoalIntentDto? intent, bool requireIntentStoreEntry)
    {
        var status = BuildStatus();
        if (status.Status != CloudReadonlySandboxControlledTrialStatuses.Ready)
        {
            return Result.Invalid($"CloudReadonlySandboxControlledTrial is not ready. Status={status.Status}.");
        }

        if (intent is null || string.IsNullOrWhiteSpace(intent.IntentId))
        {
            return Result.Invalid("CloudSandboxGoalIntent is required for controlled sandbox trial plans.");
        }

        var stored = intentStore.Get(intent.IntentId);
        if (requireIntentStoreEntry &&
            (stored is null || !string.Equals(stored.GoalHash, intent.GoalHash, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Invalid("CloudSandboxGoalIntent was not created by the controlled sandbox intent gate.");
        }

        if (intent.RejectedReasons.Count > 0)
        {
            return Result.Invalid("CloudSandboxGoalIntent contains rejected reasons and cannot be planned.");
        }

        var options = controlledOptions.Value;
        var allowedEndpoints = FilterAllowedEndpointCodes(options.AllowedEndpointCodes);
        if (intent.EndpointCodes.Count == 0 ||
            intent.EndpointCodes.Any(code => !allowedEndpoints.Contains(code, StringComparer.OrdinalIgnoreCase)))
        {
            return Result.Invalid("CloudSandboxGoalIntent endpoint is outside the controlled sandbox allowlist.");
        }

        if (intent.MaxRows < 1 || intent.MaxRows > options.MaxRows)
        {
            return Result.Invalid("CloudSandboxGoalIntent maxRows is outside the controlled sandbox limit.");
        }

        return Result.Success();
    }

    public Task<Result<CloudReadonlySandboxAgentTrialResultDto>> RunIntentAsync(
        string intentId,
        IReadOnlyCollection<string>? artifactTypes,
        int maxRows,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var intent = intentStore.Get(intentId);
        return intent is null
            ? Task.FromResult((Result<CloudReadonlySandboxAgentTrialResultDto>)Result.Invalid("CloudSandboxGoalIntent was not found or has expired."))
            : RunIntentAsync(intent, artifactTypes, maxRows, timeoutMs, cancellationToken);
    }

    public async Task<Result<CloudReadonlySandboxAgentTrialResultDto>> RunIntentAsync(
        CloudSandboxGoalIntentDto intent,
        IReadOnlyCollection<string>? artifactTypes,
        int maxRows,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var validation = ValidateIntentForExecution(intent);
        if (!validation.IsSuccess)
        {
            return Result.From(validation);
        }

        var endpointCode = intent.EndpointCodes.First();
        if (!EndpointSpecs.TryGetValue(endpointCode, out var endpoint) || endpoint.IsBlockedByPolicy)
        {
            return Result.Invalid("CloudReadonlySandboxControlledTrial endpoint is blocked by policy.");
        }

        var options = controlledOptions.Value;
        var effectiveMaxRows = Math.Clamp(maxRows <= 0 ? intent.MaxRows : maxRows, 1, options.MaxRows);
        var effectiveTimeoutMs = Math.Clamp(timeoutMs <= 0 ? options.TimeoutMs : timeoutMs, 500, options.TimeoutMs);
        var query = BuildQuery(intent, effectiveMaxRows);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(effectiveTimeoutMs));
            using var document = await cloudReadonlySandboxClient.SendJsonAsync(
                cloudReadonlySandboxOptions.Value,
                endpoint.Method,
                endpoint.Path,
                query,
                timeoutCts.Token);
            stopwatch.Stop();

            var (rows, sourceTruncated) = ExtractRows(document.RootElement, effectiveMaxRows, endpoint.Code);
            var resultHash = ComputeHash(JsonSerializer.Serialize(rows));
            var queryHash = ComputeHash($"{intent.IntentId}|{intent.GoalHash}|{endpoint.Code}|{effectiveMaxRows}|{CloudReadonlySandboxControlledTrialMarkers.Boundary}");
            var result = new CloudSandboxQueryResultDto(
                endpoint.Code,
                CloudReadonlySandboxAgentTrialMarkers.SourceType,
                CloudReadonlySandboxAgentTrialMarkers.SourceMode,
                IsSandbox: true,
                IsSimulation: false,
                CloudReadonlySandboxAgentTrialMarkers.SourceLabel,
                CloudReadonlySandboxControlledTrialMarkers.Boundary,
                queryHash,
                resultHash,
                rows.Count,
                sourceTruncated,
                rows,
                DateTimeOffset.UtcNow,
                stopwatch.ElapsedMilliseconds,
                "ToolApprovalRequired");

            var artifactRejected = new List<string>();
            return Result.Success(new CloudReadonlySandboxAgentTrialResultDto(
                intent.IntentId,
                intent.AnalysisType,
                CloudReadonlySandboxControlledTrialStatuses.Completed,
                result,
                NormalizeArtifactTypes(artifactTypes, options, artifactRejected),
                CloudReadonlySandboxControlledTrialMarkers.Boundary));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure(new ApiProblemDescriptor(
                CloudAiReadProblemCodes.Unavailable,
                "CloudReadonlySandboxControlledTrial request timed out."));
        }
        catch (CloudAiReadException ex)
        {
            return Result.Failure(new ApiProblemDescriptor(
                ex.Code,
                $"CloudReadonlySandboxControlledTrial query failed: {ex.Message}"));
        }
        catch (JsonException)
        {
            return Result.Failure(new ApiProblemDescriptor(
                CloudAiReadProblemCodes.Unavailable,
                "CloudReadonlySandboxControlledTrial response shape is invalid JSON."));
        }
    }

    private void ValidateProductionBoundary(ICollection<string> errors)
    {
        var cloudReadonly = cloudReadonlyOptions.Value;
        var cloudAiRead = cloudAiReadOptions.Value;

        if (cloudReadonly.Mode != CloudReadonlyDataSourceMode.Disabled)
        {
            errors.Add("CloudReadonly.Mode must remain Disabled during P8 controlled sandbox trial.");
        }

        if (cloudReadonly.Real.Enabled)
        {
            errors.Add("CloudReadonly.Real.Enabled must remain false during P8 controlled sandbox trial.");
        }

        if (cloudReadonly.Real.AllowProductionRead)
        {
            errors.Add("CloudReadonly.Real.AllowProductionRead must remain false during P8 controlled sandbox trial.");
        }

        if (cloudAiRead.Enabled)
        {
            errors.Add("CloudAiRead.Enabled must remain false during P8 controlled sandbox trial; use CloudReadonlySandbox only.");
        }
    }

    private static string[] FilterAllowedEndpointCodes(IEnumerable<string>? endpointCodes)
    {
        return (endpointCodes ?? [])
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Where(code => EndpointSpecs.TryGetValue(code, out var spec) && !spec.IsBlockedByPolicy)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeGoal(string? goal) =>
        string.Join(' ', (goal ?? string.Empty).Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));

    private static bool ContainsBlockedGoalTerm(string goal)
    {
        var terms = new[]
        {
            "recipe",
            "配方",
            "版本历史",
            "recipe version",
            "write",
            "update",
            "delete",
            "drop",
            "insert",
            "写入",
            "创建",
            "更新",
            "删除",
            "生产读取",
            "production cloud",
            "real cloud",
            "prod cloud"
        };
        return terms.Any(term => goal.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolveEndpointCode(string goal)
    {
        if (goal.Contains("过站", StringComparison.OrdinalIgnoreCase) ||
            goal.Contains("pass", StringComparison.OrdinalIgnoreCase))
        {
            return "pass_station_records";
        }

        if (goal.Contains("日志", StringComparison.OrdinalIgnoreCase) ||
            goal.Contains("异常", StringComparison.OrdinalIgnoreCase) ||
            goal.Contains("告警", StringComparison.OrdinalIgnoreCase) ||
            goal.Contains("停机", StringComparison.OrdinalIgnoreCase) ||
            goal.Contains("log", StringComparison.OrdinalIgnoreCase) ||
            goal.Contains("alarm", StringComparison.OrdinalIgnoreCase) ||
            goal.Contains("exception", StringComparison.OrdinalIgnoreCase))
        {
            return "device_logs";
        }

        if (goal.Contains("产能", StringComparison.OrdinalIgnoreCase) ||
            goal.Contains("交付", StringComparison.OrdinalIgnoreCase) ||
            goal.Contains("capacity", StringComparison.OrdinalIgnoreCase) ||
            goal.Contains("delivery", StringComparison.OrdinalIgnoreCase))
        {
            return "capacity_summary";
        }

        if (goal.Contains("设备", StringComparison.OrdinalIgnoreCase) ||
            goal.Contains("device", StringComparison.OrdinalIgnoreCase))
        {
            return "devices";
        }

        return null;
    }

    private static string ResolveAnalysisType(string goal, string? endpointCode)
    {
        if (endpointCode == "device_logs" && goal.Contains("异常", StringComparison.OrdinalIgnoreCase))
        {
            return "DeviceExceptionAnalysis";
        }

        if (endpointCode == "capacity_summary" && goal.Contains("交付", StringComparison.OrdinalIgnoreCase))
        {
            return "CapacityDeliveryAnalysis";
        }

        return endpointCode switch
        {
            "devices" => "DeviceList",
            "capacity_summary" => "CapacitySummary",
            "device_logs" => "DeviceLogs",
            "pass_station_records" => "PassStationRecords",
            _ => "Unknown"
        };
    }

    private static IReadOnlyCollection<string> NormalizeArtifactTypes(
        IReadOnlyCollection<string>? requested,
        CloudReadonlySandboxControlledTrialOptions options,
        ICollection<string> rejected)
    {
        var allowed = new HashSet<string>(options.AllowedArtifactTypes, StringComparer.OrdinalIgnoreCase);
        var values = requested is { Count: > 0 } ? requested : ["Markdown", "Html"];
        var normalized = new List<string>();
        foreach (var item in values)
        {
            if (string.IsNullOrWhiteSpace(item))
            {
                continue;
            }

            var trimmed = item.Trim();
            if (!allowed.Contains(trimmed))
            {
                rejected.Add($"Artifact type '{trimmed}' is not allowed in CloudReadonlySandboxControlledTrial.");
                continue;
            }

            normalized.Add(trimmed);
        }

        return normalized
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static CloudSandboxGoalTimeRangeDto NormalizeTimeRange(
        CloudSandboxGoalTimeRangeDto? requested,
        CloudReadonlySandboxControlledTrialOptions options,
        ICollection<string> rejected,
        ICollection<string> warnings)
    {
        var now = DateTimeOffset.UtcNow;
        var from = requested?.From ?? now.AddDays(-7);
        var to = requested?.To ?? now;

        if (from > to)
        {
            rejected.Add("timeRange.from must be earlier than timeRange.to.");
        }

        if (to - from > TimeSpan.FromDays(options.MaxTimeRangeDays))
        {
            rejected.Add($"timeRange cannot exceed {options.MaxTimeRangeDays} days.");
        }

        if (requested is null)
        {
            warnings.Add("timeRange was not provided; defaulted to the last 7 days.");
        }

        return new CloudSandboxGoalTimeRangeDto(from, to);
    }

    private static IReadOnlyDictionary<string, string?> BuildQuery(CloudSandboxGoalIntentDto intent, int maxRows)
    {
        return new Dictionary<string, string?>
        {
            ["intentId"] = intent.IntentId,
            ["goalHash"] = intent.GoalHash,
            ["analysisType"] = intent.AnalysisType,
            ["maxRows"] = maxRows.ToString(),
            ["from"] = intent.TimeRange.From?.ToString("O"),
            ["to"] = intent.TimeRange.To?.ToString("O"),
            ["boundary"] = CloudReadonlySandboxControlledTrialMarkers.Boundary
        };
    }

    private static (IReadOnlyCollection<IReadOnlyDictionary<string, object?>> Rows, bool IsTruncated) ExtractRows(
        JsonElement root,
        int maxRows,
        string endpointCode)
    {
        var sourceRows = EnumerateRows(root).ToArray();
        var isTruncated = ReadIsTruncated(root) || sourceRows.Length > maxRows;
        var rows = sourceRows
            .Take(maxRows)
            .Select(row =>
            {
                var normalized = new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase)
                {
                    ["sourceType"] = CloudReadonlySandboxAgentTrialMarkers.SourceType,
                    ["sourceMode"] = CloudReadonlySandboxAgentTrialMarkers.SourceMode,
                    ["isSandbox"] = true,
                    ["isSimulation"] = false,
                    ["sourceLabel"] = CloudReadonlySandboxAgentTrialMarkers.SourceLabel,
                    ["boundary"] = CloudReadonlySandboxControlledTrialMarkers.Boundary,
                    ["endpointCode"] = endpointCode
                };
                return (IReadOnlyDictionary<string, object?>)normalized;
            })
            .ToArray();

        return (rows, isTruncated);
    }

    private static IEnumerable<IReadOnlyDictionary<string, object?>> EnumerateRows(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                yield return ReadObject(item);
            }

            yield break;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var propertyName in new[] { "items", "rows", "data" })
        {
            if (root.TryGetProperty(propertyName, out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in array.EnumerateArray())
                {
                    yield return ReadObject(item);
                }

                yield break;
            }
        }

        yield return ReadObject(root);
    }

    private static bool ReadIsTruncated(JsonElement root)
    {
        return root.ValueKind == JsonValueKind.Object &&
               root.TryGetProperty("isTruncated", out var value) &&
               value.ValueKind == JsonValueKind.True;
    }

    private static IReadOnlyDictionary<string, object?> ReadObject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, object?> { ["value"] = ReadValue(element) };
        }

        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            result[property.Name] = ReadValue(property.Value);
        }

        return result;
    }

    private static object? ReadValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when value.TryGetDouble(out var doubleValue) => doubleValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => value.GetRawText()
        };
    }

    private static string ComputeHash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private sealed record EndpointSpec(
        string Code,
        HttpMethod Method,
        string Path,
        bool IsBlockedByPolicy = false);
}
