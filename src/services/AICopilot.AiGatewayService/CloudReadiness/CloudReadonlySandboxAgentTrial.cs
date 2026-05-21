using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Options;

namespace AICopilot.AiGatewayService.CloudReadiness;

public static class CloudReadonlySandboxAgentTrialStatuses
{
    public const string Disabled = "Disabled";
    public const string Ready = "Ready";
    public const string SandboxSmokeRequired = "SandboxSmokeRequired";
    public const string Blocked = "Blocked";
    public const string Failed = "Failed";
    public const string Completed = "Completed";
}

public sealed record CloudReadonlySandboxAgentTrialStatusDto(
    string Status,
    string SandboxSmokeStatus,
    bool TrialEnabled,
    IReadOnlyCollection<string> AvailableScenarioIds,
    bool ToolVisible,
    bool ToolExecutable,
    DateTimeOffset? LastTrialAt,
    IReadOnlyCollection<string> Errors,
    IReadOnlyCollection<string> Warnings,
    string Boundary = CloudReadonlySandboxAgentTrialMarkers.Boundary);

public sealed record CloudSandboxQueryResultDto(
    string EndpointCode,
    string SourceType,
    string SourceMode,
    bool IsSandbox,
    bool IsSimulation,
    string SourceLabel,
    string Boundary,
    string QueryHash,
    string ResultHash,
    int RowCount,
    bool IsTruncated,
    IReadOnlyCollection<IReadOnlyDictionary<string, object?>> Rows,
    DateTimeOffset ExecutedAt,
    long DurationMs,
    string ApprovalStatus);

public sealed record CloudReadonlySandboxAgentTrialResultDto(
    string ScenarioId,
    string ScenarioTitle,
    string Status,
    CloudSandboxQueryResultDto QueryResult,
    IReadOnlyCollection<string> ArtifactTypes,
    string Boundary = CloudReadonlySandboxAgentTrialMarkers.Boundary);

[AuthorizeRequirement("AiGateway.ToolRegistry.Read")]
public sealed record GetCloudReadonlySandboxAgentTrialStatusQuery
    : IQuery<Result<CloudReadonlySandboxAgentTrialStatusDto>>;

[AuthorizeRequirement("AiGateway.RunAgentTask")]
public sealed record RunCloudReadonlySandboxAgentTrialCommand(
    string ScenarioId,
    IReadOnlyCollection<string>? ArtifactTypes = null,
    int MaxRows = 20,
    int TimeoutMs = 5000,
    string TrialMode = CloudReadonlySandboxControlledTrialMarkers.FixedScenarioTrialMode,
    string? IntentId = null) : ICommand<Result<CloudReadonlySandboxAgentTrialResultDto>>;

public sealed class GetCloudReadonlySandboxAgentTrialStatusQueryHandler(
    CloudReadonlySandboxAgentTrialService trialService)
    : IQueryHandler<GetCloudReadonlySandboxAgentTrialStatusQuery, Result<CloudReadonlySandboxAgentTrialStatusDto>>
{
    public Task<Result<CloudReadonlySandboxAgentTrialStatusDto>> Handle(
        GetCloudReadonlySandboxAgentTrialStatusQuery request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success(trialService.BuildStatus()));
    }
}

public sealed class RunCloudReadonlySandboxAgentTrialCommandHandler(
    CloudReadonlySandboxAgentTrialService trialService,
    CloudReadonlySandboxControlledTrialService controlledTrialService,
    ICloudReadonlySandboxAgentTrialHistoryStore historyStore)
    : ICommandHandler<RunCloudReadonlySandboxAgentTrialCommand, Result<CloudReadonlySandboxAgentTrialResultDto>>
{
    public async Task<Result<CloudReadonlySandboxAgentTrialResultDto>> Handle(
        RunCloudReadonlySandboxAgentTrialCommand request,
        CancellationToken cancellationToken)
    {
        var result = string.Equals(
                request.TrialMode,
                CloudReadonlySandboxControlledTrialMarkers.TrialMode,
                StringComparison.OrdinalIgnoreCase)
            ? await controlledTrialService.RunIntentAsync(
                request.IntentId ?? request.ScenarioId,
                request.ArtifactTypes,
                request.MaxRows,
                request.TimeoutMs,
                cancellationToken)
            : await trialService.RunScenarioAsync(
                request.ScenarioId,
                request.ArtifactTypes,
                request.MaxRows,
                request.TimeoutMs,
                cancellationToken);
        if (result.IsSuccess && result.Value is not null)
        {
            historyStore.Save(result.Value);
        }

        return result;
    }
}

public interface ICloudReadonlySandboxAgentTrialHistoryStore
{
    void Save(CloudReadonlySandboxAgentTrialResultDto result);

    IReadOnlyCollection<CloudReadonlySandboxAgentTrialResultDto> List();
}

internal sealed class InMemoryCloudReadonlySandboxAgentTrialHistoryStore
    : ICloudReadonlySandboxAgentTrialHistoryStore
{
    private readonly object sync = new();
    private readonly List<CloudReadonlySandboxAgentTrialResultDto> items = [];

    public void Save(CloudReadonlySandboxAgentTrialResultDto result)
    {
        lock (sync)
        {
            items.Insert(0, result);
            if (items.Count > 20)
            {
                items.RemoveRange(20, items.Count - 20);
            }
        }
    }

    public IReadOnlyCollection<CloudReadonlySandboxAgentTrialResultDto> List()
    {
        lock (sync)
        {
            return items.ToArray();
        }
    }
}

public sealed class CloudReadonlySandboxAgentTrialService(
    IOptions<CloudReadonlyOptions> cloudReadonlyOptions,
    IOptions<CloudReadonlySandboxOptions> cloudReadonlySandboxOptions,
    IOptions<CloudAiReadOptions> cloudAiReadOptions,
    IOptions<CloudReadonlySandboxAgentTrialOptions> trialOptions,
    ICloudReadonlyReadinessHistoryStore readinessHistoryStore,
    ICloudReadonlySandboxAgentTrialHistoryStore trialHistoryStore,
    ICloudReadonlySandboxClient cloudReadonlySandboxClient)
{
    private static readonly IReadOnlyDictionary<string, SandboxTrialScenario> Scenarios =
        new[]
        {
            new SandboxTrialScenario(
                "cloud-sandbox-devices",
                "设备清单",
                "Device",
                "devices",
                ["Markdown", "Html"]),
            new SandboxTrialScenario(
                "cloud-sandbox-capacity-summary",
                "产能汇总",
                "Capacity",
                "capacity_summary",
                ["Chart", "Markdown", "Html", "Pptx"]),
            new SandboxTrialScenario(
                "cloud-sandbox-device-logs",
                "设备日志",
                "Equipment",
                "device_logs",
                ["Markdown", "Html", "Xlsx"]),
            new SandboxTrialScenario(
                "cloud-sandbox-pass-station-records",
                "过站记录",
                "Production",
                "pass_station_records",
                ["Chart", "Markdown", "Html", "Xlsx"]),
            new SandboxTrialScenario(
                "cloud-sandbox-device-exception-analysis",
                "设备异常分析",
                "Equipment",
                "device_logs",
                ["Chart", "Markdown", "Html", "Pdf"]),
            new SandboxTrialScenario(
                "cloud-sandbox-capacity-delivery-analysis",
                "产能交付分析",
                "Delivery",
                "capacity_summary",
                ["Chart", "Markdown", "Html", "Pptx", "Xlsx"])
        }.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);

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

    public static IReadOnlyCollection<string> ScenarioIds => Scenarios.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();

    public static bool IsScenarioId(string? scenarioId) =>
        !string.IsNullOrWhiteSpace(scenarioId) && Scenarios.ContainsKey(scenarioId);

    public static string? ResolveScenarioTitle(string? scenarioId) =>
        !string.IsNullOrWhiteSpace(scenarioId) && Scenarios.TryGetValue(scenarioId, out var scenario)
            ? scenario.Title
            : null;

    public static string? ResolveScenarioDomain(string? scenarioId) =>
        !string.IsNullOrWhiteSpace(scenarioId) && Scenarios.TryGetValue(scenarioId, out var scenario)
            ? scenario.BusinessDomain
            : null;

    public static IReadOnlyCollection<string> ResolveScenarioArtifactTypes(string? scenarioId) =>
        !string.IsNullOrWhiteSpace(scenarioId) && Scenarios.TryGetValue(scenarioId, out var scenario)
            ? scenario.ArtifactTypes
            : [];

    public CloudReadonlySandboxAgentTrialStatusDto BuildStatus()
    {
        var options = trialOptions.Value;
        var sandbox = cloudReadonlySandboxOptions.Value;
        var latestSmoke = readinessHistoryStore.List()
            .FirstOrDefault(item => item.Mode == CloudReadonlyReadinessModes.RealSandboxSmoke);
        var latestTrial = trialHistoryStore.List().FirstOrDefault();
        var errors = new List<string>();
        var warnings = new List<string>();

        ValidateProductionBoundary(errors);

        if (!sandbox.IsConfigured())
        {
            warnings.Add("CloudReadonlySandbox is not configured; sandbox agent trial requires a passed sandbox smoke first.");
        }

        var sandboxSmokeStatus = latestSmoke?.Status ?? CloudReadonlyReadinessStatuses.NotConfigured;
        var smokePassed = string.Equals(sandboxSmokeStatus, CloudReadonlyReadinessStatuses.RealSandboxPassed, StringComparison.Ordinal);
        if (!smokePassed)
        {
            warnings.Add("CloudReadonlySandbox RealSandboxSmoke has not passed in this process.");
        }

        if (!options.Enabled)
        {
            return new CloudReadonlySandboxAgentTrialStatusDto(
                CloudReadonlySandboxAgentTrialStatuses.Disabled,
                sandboxSmokeStatus,
                TrialEnabled: false,
                AvailableScenarioIds: [],
                ToolVisible: false,
                ToolExecutable: false,
                LastTrialAt: latestTrial?.QueryResult.ExecutedAt,
                Errors: errors,
                Warnings: warnings);
        }

        var allowedScenarioIds = FilterAllowedScenarioIds(options.AllowedScenarioIds);
        if (allowedScenarioIds.Length == 0)
        {
            errors.Add("CloudReadonlySandboxAgentTrial has no allowed fixed scenarios.");
        }

        var status = errors.Count > 0
            ? CloudReadonlySandboxAgentTrialStatuses.Blocked
            : smokePassed && sandbox.IsConfigured()
                ? CloudReadonlySandboxAgentTrialStatuses.Ready
                : CloudReadonlySandboxAgentTrialStatuses.SandboxSmokeRequired;

        return new CloudReadonlySandboxAgentTrialStatusDto(
            status,
            sandboxSmokeStatus,
            TrialEnabled: options.Enabled,
            AvailableScenarioIds: status == CloudReadonlySandboxAgentTrialStatuses.Ready ? allowedScenarioIds : [],
            ToolVisible: status == CloudReadonlySandboxAgentTrialStatuses.Ready,
            ToolExecutable: status == CloudReadonlySandboxAgentTrialStatuses.Ready,
            LastTrialAt: latestTrial?.QueryResult.ExecutedAt,
            Errors: errors,
            Warnings: warnings);
    }

    public async Task<Result<CloudReadonlySandboxAgentTrialResultDto>> RunScenarioAsync(
        string scenarioId,
        IReadOnlyCollection<string>? artifactTypes,
        int maxRows,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var status = BuildStatus();
        if (status.Status != CloudReadonlySandboxAgentTrialStatuses.Ready)
        {
            return Result.Invalid($"CloudReadonlySandboxAgentTrial is not ready. Status={status.Status}; Smoke={status.SandboxSmokeStatus}.");
        }

        var options = trialOptions.Value;
        var normalizedScenarioId = scenarioId?.Trim() ?? string.Empty;
        if (!Scenarios.TryGetValue(normalizedScenarioId, out var scenario) ||
            !FilterAllowedScenarioIds(options.AllowedScenarioIds).Contains(normalizedScenarioId, StringComparer.OrdinalIgnoreCase))
        {
            return Result.Invalid("CloudReadonlySandboxAgentTrial only allows fixed sandbox trial scenarios.");
        }

        if (!EndpointSpecs.TryGetValue(scenario.EndpointCode, out var endpoint) || endpoint.IsBlockedByPolicy)
        {
            return Result.Invalid("CloudReadonlySandboxAgentTrial endpoint is blocked by policy.");
        }

        var effectiveMaxRows = Math.Clamp(maxRows <= 0 ? options.MaxRows : maxRows, 1, options.MaxRows);
        var effectiveTimeoutMs = Math.Clamp(timeoutMs <= 0 ? options.TimeoutMs : timeoutMs, 500, options.TimeoutMs);
        var query = BuildQuery(scenario, effectiveMaxRows);
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
            var queryHash = ComputeHash($"{normalizedScenarioId}|{endpoint.Code}|{effectiveMaxRows}|{CloudReadonlySandboxAgentTrialMarkers.Boundary}");
            var now = DateTimeOffset.UtcNow;
            var result = new CloudSandboxQueryResultDto(
                endpoint.Code,
                CloudReadonlySandboxAgentTrialMarkers.SourceType,
                CloudReadonlySandboxAgentTrialMarkers.SourceMode,
                IsSandbox: true,
                IsSimulation: false,
                CloudReadonlySandboxAgentTrialMarkers.SourceLabel,
                CloudReadonlySandboxAgentTrialMarkers.Boundary,
                queryHash,
                resultHash,
                rows.Count,
                sourceTruncated,
                rows,
                now,
                stopwatch.ElapsedMilliseconds,
                "ToolApprovalRequired");

            return Result.Success(new CloudReadonlySandboxAgentTrialResultDto(
                scenario.Id,
                scenario.Title,
                CloudReadonlySandboxAgentTrialStatuses.Completed,
                result,
                NormalizeArtifactTypes(artifactTypes, scenario.ArtifactTypes)));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure(new ApiProblemDescriptor(
                CloudAiReadProblemCodes.Unavailable,
                "CloudReadonlySandboxAgentTrial request timed out."));
        }
        catch (CloudAiReadException ex)
        {
            return Result.Failure(new ApiProblemDescriptor(
                ex.Code,
                $"CloudReadonlySandboxAgentTrial query failed: {ex.Message}"));
        }
        catch (JsonException)
        {
            return Result.Failure(new ApiProblemDescriptor(
                CloudAiReadProblemCodes.Unavailable,
                "CloudReadonlySandboxAgentTrial response shape is invalid JSON."));
        }
    }

    private void ValidateProductionBoundary(ICollection<string> errors)
    {
        var cloudReadonly = cloudReadonlyOptions.Value;
        var cloudAiRead = cloudAiReadOptions.Value;

        if (cloudReadonly.Mode != CloudReadonlyDataSourceMode.Disabled)
        {
            errors.Add("CloudReadonly.Mode must remain Disabled during P7 sandbox agent trial.");
        }

        if (cloudReadonly.Real.Enabled)
        {
            errors.Add("CloudReadonly.Real.Enabled must remain false during P7 sandbox agent trial.");
        }

        if (cloudReadonly.Real.AllowProductionRead)
        {
            errors.Add("CloudReadonly.Real.AllowProductionRead must remain false during P7 sandbox agent trial.");
        }

        if (cloudAiRead.Enabled)
        {
            errors.Add("CloudAiRead.Enabled must remain false during P7 sandbox agent trial; use CloudReadonlySandbox only.");
        }
    }

    private static string[] FilterAllowedScenarioIds(IEnumerable<string>? allowedScenarioIds)
    {
        return (allowedScenarioIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Where(Scenarios.ContainsKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string?> BuildQuery(SandboxTrialScenario scenario, int maxRows)
    {
        return new Dictionary<string, string?>
        {
            ["scenarioId"] = scenario.Id,
            ["maxRows"] = maxRows.ToString(),
            ["boundary"] = CloudReadonlySandboxAgentTrialMarkers.Boundary
        };
    }

    private static IReadOnlyCollection<string> NormalizeArtifactTypes(
        IReadOnlyCollection<string>? requested,
        IReadOnlyCollection<string> defaults)
    {
        var allowed = new HashSet<string>(["Chart", "Markdown", "Html", "Pdf", "Pptx", "Xlsx"], StringComparer.OrdinalIgnoreCase);
        var values = requested is { Count: > 0 } ? requested : defaults;
        return values
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Where(item => allowed.Contains(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
                    ["boundary"] = CloudReadonlySandboxAgentTrialMarkers.Boundary,
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

    private sealed record SandboxTrialScenario(
        string Id,
        string Title,
        string BusinessDomain,
        string EndpointCode,
        IReadOnlyCollection<string> ArtifactTypes);

    private sealed record EndpointSpec(
        string Code,
        HttpMethod Method,
        string Path,
        bool IsBlockedByPolicy = false);
}
