using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AICopilot.AiGatewayService.TrialOperations;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Options;

namespace AICopilot.AiGatewayService.CloudReadiness;

public static class CloudReadonlyProductionPilotStatuses
{
    public const string Disabled = "Disabled";
    public const string NotConfigured = "NotConfigured";
    public const string P11GateRequired = "P11GateRequired";
    public const string WindowPendingApproval = "WindowPendingApproval";
    public const string WindowNotStarted = "WindowNotStarted";
    public const string Ready = "Ready";
    public const string Paused = "Paused";
    public const string Expired = "Expired";
    public const string EmergencyStopped = "EmergencyStopped";
    public const string Blocked = "Blocked";
    public const string Failed = "Failed";
    public const string Completed = "Completed";
}

public static class CloudReadonlyProductionPilotWindowStatuses
{
    public const string PendingApproval = "PendingApproval";
    public const string Approved = "Approved";
    public const string Paused = "Paused";
    public const string Completed = "Completed";
    public const string EmergencyStopped = "EmergencyStopped";
}

public sealed record CloudReadonlyProductionPilotStatusDto(
    string Status,
    bool Enabled,
    string? PilotWindowId,
    string? WindowStatus,
    IReadOnlyCollection<string> AllowedEndpointCodes,
    string ApprovalStatus,
    bool ToolVisible,
    bool ToolExecutable,
    DateTimeOffset? LastRunAt,
    IReadOnlyCollection<string> Blockers,
    IReadOnlyCollection<string> Warnings);

public sealed record CloudReadonlyProductionPilotWindowDto(
    string WindowId,
    string Name,
    string Status,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    IReadOnlyCollection<string> AllowedEndpointCodes,
    int MaxTimeRangeDays,
    int MaxRows,
    int TimeoutMs,
    string OwnerDepartment,
    string ApprovalPolicy,
    string RollbackPolicy);

public sealed record CloudProductionPilotTimeRangeDto(DateTimeOffset? From, DateTimeOffset? To);

public sealed record CloudProductionPilotQueryResultDto(
    string EndpointCode,
    string SourceType,
    string SourceMode,
    bool IsProductionData,
    bool IsSandbox,
    bool IsSimulation,
    string SourceLabel,
    string Boundary,
    string PilotWindowId,
    string QueryHash,
    string ResultHash,
    int RowCount,
    bool IsTruncated,
    IReadOnlyCollection<IReadOnlyDictionary<string, object?>> Rows,
    DateTimeOffset ExecutedAt,
    long DurationMs,
    string ApprovalStatus);

public sealed record CloudReadonlyProductionPilotScenarioResultDto(
    string ScenarioId,
    string ScenarioTitle,
    string Status,
    CloudProductionPilotQueryResultDto QueryResult,
    IReadOnlyCollection<string> ArtifactTypes,
    string Boundary = CloudReadonlyProductionPilotMarkers.Boundary);

[AuthorizeRequirement(TrialOperationsPermissions.Read)]
public sealed record GetCloudReadonlyProductionPilotStatusQuery
    : IQuery<Result<CloudReadonlyProductionPilotStatusDto>>;

[AuthorizeRequirement(TrialOperationsPermissions.Manage)]
public sealed record CreateCloudReadonlyProductionPilotWindowCommand(
    string? Name = null,
    DateTimeOffset? StartAt = null,
    DateTimeOffset? EndAt = null,
    IReadOnlyCollection<string>? AllowedEndpointCodes = null,
    int? MaxTimeRangeDays = null,
    int? MaxRows = null,
    int? TimeoutMs = null,
    string? OwnerDepartment = null,
    string? ApprovalPolicy = null,
    string? RollbackPolicy = null) : ICommand<Result<CloudReadonlyProductionPilotWindowDto>>;

[AuthorizeRequirement(TrialOperationsPermissions.Manage)]
public sealed record UpdateCloudReadonlyProductionPilotWindowStatusCommand(
    string WindowId,
    string Status) : ICommand<Result<CloudReadonlyProductionPilotWindowDto>>;

[AuthorizeRequirement(TrialOperationsPermissions.AuditView)]
public sealed record RunCloudReadonlyProductionPilotGateEvaluationCommand
    : ICommand<Result<CloudReadonlyProductionPilotStatusDto>>;

[AuthorizeRequirement("AiGateway.RunAgentTask")]
public sealed record RunCloudReadonlyProductionPilotScenarioCommand(
    string ScenarioId,
    IReadOnlyCollection<string>? ArtifactTypes = null,
    string? PilotWindowId = null,
    CloudProductionPilotTimeRangeDto? TimeRange = null,
    int MaxRows = 20,
    int TimeoutMs = 5000) : ICommand<Result<CloudReadonlyProductionPilotScenarioResultDto>>;

public sealed class GetCloudReadonlyProductionPilotStatusQueryHandler(
    CloudReadonlyProductionPilotService productionPilotService,
    CloudReadonlyPilotReadinessService pilotReadinessService,
    IReadRepository<ToolRegistration> toolRepository)
    : IQueryHandler<GetCloudReadonlyProductionPilotStatusQuery, Result<CloudReadonlyProductionPilotStatusDto>>
{
    public async Task<Result<CloudReadonlyProductionPilotStatusDto>> Handle(
        GetCloudReadonlyProductionPilotStatusQuery request,
        CancellationToken cancellationToken)
    {
        var protectedTools = await GetCloudReadonlyPilotReadinessQueryHandler.LoadProtectedToolRegistrationsAsync(
            toolRepository,
            cancellationToken);
        return Result.Success(productionPilotService.BuildStatus(pilotReadinessService.BuildStatus(protectedTools), protectedTools));
    }
}

public sealed class CreateCloudReadonlyProductionPilotWindowCommandHandler(
    CloudReadonlyProductionPilotService productionPilotService,
    CloudReadonlyPilotReadinessService pilotReadinessService,
    IReadRepository<ToolRegistration> toolRepository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<CreateCloudReadonlyProductionPilotWindowCommand, Result<CloudReadonlyProductionPilotWindowDto>>
{
    public async Task<Result<CloudReadonlyProductionPilotWindowDto>> Handle(
        CreateCloudReadonlyProductionPilotWindowCommand request,
        CancellationToken cancellationToken)
    {
        var protectedTools = await GetCloudReadonlyPilotReadinessQueryHandler.LoadProtectedToolRegistrationsAsync(
            toolRepository,
            cancellationToken);
        var p11Status = pilotReadinessService.BuildStatus(protectedTools);
        var result = productionPilotService.CreateWindow(request, p11Status, protectedTools);
        if (!result.IsSuccess || result.Value is null)
        {
            return result;
        }

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                "AiGateway.CreateCloudReadonlyProductionPilotWindow",
                "CloudReadonlyProductionPilot",
                result.Value.WindowId,
                result.Value.Status,
                AuditResults.Succeeded,
                $"Created P12 production readonly Pilot window; windowId={result.Value.WindowId}; endpoints={string.Join(",", result.Value.AllowedEndpointCodes)}.",
                ["windowId", "allowedEndpointCodes", "status"]),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        return result;
    }
}

public sealed class UpdateCloudReadonlyProductionPilotWindowStatusCommandHandler(
    CloudReadonlyProductionPilotService productionPilotService,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<UpdateCloudReadonlyProductionPilotWindowStatusCommand, Result<CloudReadonlyProductionPilotWindowDto>>
{
    public async Task<Result<CloudReadonlyProductionPilotWindowDto>> Handle(
        UpdateCloudReadonlyProductionPilotWindowStatusCommand request,
        CancellationToken cancellationToken)
    {
        var result = productionPilotService.UpdateWindowStatus(request.WindowId, request.Status);
        if (!result.IsSuccess || result.Value is null)
        {
            return result;
        }

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                "AiGateway.UpdateCloudReadonlyProductionPilotWindowStatus",
                "CloudReadonlyProductionPilot",
                result.Value.WindowId,
                result.Value.Status,
                AuditResults.Succeeded,
                $"Updated P12 production readonly Pilot window status; windowId={result.Value.WindowId}; status={result.Value.Status}.",
                ["windowId", "status"]),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        return result;
    }
}

public sealed class RunCloudReadonlyProductionPilotGateEvaluationCommandHandler(
    CloudReadonlyProductionPilotService productionPilotService,
    CloudReadonlyPilotReadinessService pilotReadinessService,
    IReadRepository<ToolRegistration> toolRepository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<RunCloudReadonlyProductionPilotGateEvaluationCommand, Result<CloudReadonlyProductionPilotStatusDto>>
{
    public async Task<Result<CloudReadonlyProductionPilotStatusDto>> Handle(
        RunCloudReadonlyProductionPilotGateEvaluationCommand request,
        CancellationToken cancellationToken)
    {
        var protectedTools = await GetCloudReadonlyPilotReadinessQueryHandler.LoadProtectedToolRegistrationsAsync(
            toolRepository,
            cancellationToken);
        var status = productionPilotService.BuildStatus(
            pilotReadinessService.BuildStatus(protectedTools),
            protectedTools);

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                "AiGateway.RunCloudReadonlyProductionPilotGateEvaluation",
                "CloudReadonlyProductionPilot",
                status.PilotWindowId ?? "none",
                status.Status,
                status.Status == CloudReadonlyProductionPilotStatuses.Ready ? AuditResults.Succeeded : AuditResults.Rejected,
                $"Ran P12 production readonly Pilot gate; status={status.Status}; blockers={status.Blockers.Count}.",
                ["status", "blockers", "windowId"]),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        return Result.Success(status);
    }
}

public sealed class RunCloudReadonlyProductionPilotScenarioCommandHandler(
    CloudReadonlyProductionPilotService productionPilotService,
    CloudReadonlyPilotReadinessService pilotReadinessService,
    IReadRepository<ToolRegistration> toolRepository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<RunCloudReadonlyProductionPilotScenarioCommand, Result<CloudReadonlyProductionPilotScenarioResultDto>>
{
    public async Task<Result<CloudReadonlyProductionPilotScenarioResultDto>> Handle(
        RunCloudReadonlyProductionPilotScenarioCommand request,
        CancellationToken cancellationToken)
    {
        var protectedTools = await GetCloudReadonlyPilotReadinessQueryHandler.LoadProtectedToolRegistrationsAsync(
            toolRepository,
            cancellationToken);
        var result = await productionPilotService.RunScenarioAsync(
            request,
            pilotReadinessService.BuildStatus(protectedTools),
            protectedTools,
            cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return result;
        }

        var query = result.Value.QueryResult;
        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                "AiGateway.RunCloudReadonlyProductionPilotScenario",
                "CloudReadonlyProductionPilot",
                query.PilotWindowId,
                result.Value.Status,
                AuditResults.Succeeded,
                $"Ran P12 production readonly Pilot scenario; scenarioId={result.Value.ScenarioId}; endpoint={query.EndpointCode}; rows={query.RowCount}; truncated={query.IsTruncated}; resultHash={query.ResultHash}.",
                ["scenarioId", "endpointCode", "resultHash", "rowCount", "isTruncated"]),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        return result;
    }
}

public interface ICloudReadonlyProductionPilotStore
{
    void SaveWindow(CloudReadonlyProductionPilotWindowDto window);

    CloudReadonlyProductionPilotWindowDto? GetWindow(string windowId);

    CloudReadonlyProductionPilotWindowDto? LatestWindow();

    void SaveRun(CloudReadonlyProductionPilotScenarioResultDto result);

    IReadOnlyCollection<CloudReadonlyProductionPilotScenarioResultDto> ListRuns();
}

internal sealed class InMemoryCloudReadonlyProductionPilotStore : ICloudReadonlyProductionPilotStore
{
    private readonly object sync = new();
    private readonly Dictionary<string, CloudReadonlyProductionPilotWindowDto> windows = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CloudReadonlyProductionPilotScenarioResultDto> runs = [];

    public void SaveWindow(CloudReadonlyProductionPilotWindowDto window)
    {
        lock (sync)
        {
            windows[window.WindowId] = window;
        }
    }

    public CloudReadonlyProductionPilotWindowDto? GetWindow(string windowId)
    {
        lock (sync)
        {
            return string.IsNullOrWhiteSpace(windowId) ? null : windows.GetValueOrDefault(windowId);
        }
    }

    public CloudReadonlyProductionPilotWindowDto? LatestWindow()
    {
        lock (sync)
        {
            return windows.Values.LastOrDefault();
        }
    }

    public void SaveRun(CloudReadonlyProductionPilotScenarioResultDto result)
    {
        lock (sync)
        {
            runs.Insert(0, result);
            if (runs.Count > 20)
            {
                runs.RemoveRange(20, runs.Count - 20);
            }
        }
    }

    public IReadOnlyCollection<CloudReadonlyProductionPilotScenarioResultDto> ListRuns()
    {
        lock (sync)
        {
            return runs.ToArray();
        }
    }
}

public sealed class CloudReadonlyProductionPilotService(
    IOptions<CloudReadonlyOptions> cloudReadonlyOptions,
    IOptions<CloudAiReadOptions> cloudAiReadOptions,
    IOptions<CloudReadonlyProductionPilotOptions> pilotOptions,
    ICloudReadonlyProductionPilotStore store,
    ICloudAiReadClient cloudAiReadClient)
{
    private static readonly IReadOnlyDictionary<string, ProductionPilotScenario> Scenarios =
        new[]
        {
            new ProductionPilotScenario("cloud-production-pilot-devices", "Device list", "Device", "devices", ["Markdown", "Html"]),
            new ProductionPilotScenario("cloud-production-pilot-capacity-summary", "Capacity summary", "Capacity", "capacity_summary", ["Chart", "Markdown", "Html", "Pptx"]),
            new ProductionPilotScenario("cloud-production-pilot-device-logs", "Device logs", "Equipment", "device_logs", ["Markdown", "Html", "Xlsx"]),
            new ProductionPilotScenario("cloud-production-pilot-pass-station-records", "Pass station records", "Production", "pass_station_records", ["Chart", "Markdown", "Html", "Xlsx"]),
            new ProductionPilotScenario("cloud-production-pilot-device-exception-analysis", "Device exception analysis", "Equipment", "device_logs", ["Chart", "Markdown", "Html", "Pdf"]),
            new ProductionPilotScenario("cloud-production-pilot-capacity-delivery-analysis", "Capacity delivery analysis", "Delivery", "capacity_summary", ["Chart", "Markdown", "Html", "Pptx", "Xlsx"])
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

    public CloudReadonlyProductionPilotStatusDto BuildStatus(
        CloudReadonlyPilotReadinessStatusDto p11Status,
        IReadOnlyCollection<ToolRegistration>? persistedToolRegistrations = null)
    {
        var options = pilotOptions.Value;
        var window = store.LatestWindow();
        var latestRun = store.ListRuns().FirstOrDefault();
        var blockers = new List<string>();
        var warnings = new List<string>();

        ValidateBoundary(blockers, warnings, p11Status, persistedToolRegistrations);

        if (!options.Enabled)
        {
            return new CloudReadonlyProductionPilotStatusDto(
                CloudReadonlyProductionPilotStatuses.Disabled,
                Enabled: false,
                window?.WindowId,
                window?.Status,
                window?.AllowedEndpointCodes ?? [],
                "NotRequired",
                ToolVisible: false,
                ToolExecutable: false,
                latestRun?.QueryResult.ExecutedAt,
                blockers,
                warnings);
        }

        if (!cloudAiReadClient.IsEnabled || !cloudAiReadOptions.Value.IsConfigured())
        {
            blockers.Add("CloudAiRead must be configured before P12 production Pilot can execute.");
        }

        if (window is null)
        {
            return new CloudReadonlyProductionPilotStatusDto(
                blockers.Count > 0 ? CloudReadonlyProductionPilotStatuses.Blocked : CloudReadonlyProductionPilotStatuses.NotConfigured,
                Enabled: true,
                null,
                null,
                [],
                "Missing",
                ToolVisible: false,
                ToolExecutable: false,
                latestRun?.QueryResult.ExecutedAt,
                blockers,
                warnings);
        }

        var now = DateTimeOffset.UtcNow;
        var status = ResolveWindowStatus(window, now, blockers);
        var ready = blockers.Count == 0 && status == CloudReadonlyProductionPilotStatuses.Ready;
        return new CloudReadonlyProductionPilotStatusDto(
            blockers.Count > 0 ? CloudReadonlyProductionPilotStatuses.Blocked : status,
            Enabled: true,
            window.WindowId,
            window.Status,
            window.AllowedEndpointCodes,
            window.Status == CloudReadonlyProductionPilotWindowStatuses.Approved ? "Approved" : "Required",
            ToolVisible: ready,
            ToolExecutable: ready,
            latestRun?.QueryResult.ExecutedAt,
            blockers,
            warnings);
    }

    public Result<CloudReadonlyProductionPilotWindowDto> CreateWindow(
        CreateCloudReadonlyProductionPilotWindowCommand request,
        CloudReadonlyPilotReadinessStatusDto p11Status,
        IReadOnlyCollection<ToolRegistration>? persistedToolRegistrations = null)
    {
        var currentStatus = BuildStatus(p11Status, persistedToolRegistrations);
        if (currentStatus.Blockers.Count > 0)
        {
            return Result.Invalid($"CloudReadonlyProductionPilot gate is blocked: {string.Join("; ", currentStatus.Blockers)}");
        }

        var options = pilotOptions.Value;
        if (!options.Enabled)
        {
            return Result.Invalid("CloudReadonlyProductionPilot.Enabled is false; P12 production Pilot windows cannot be created.");
        }

        var endpoints = NormalizeEndpointCodes(request.AllowedEndpointCodes ?? options.AllowedEndpointCodes);
        if (endpoints.Length == 0)
        {
            return Result.Invalid("P12 production Pilot window must allow at least one fixed readonly endpoint.");
        }

        var now = DateTimeOffset.UtcNow;
        var startAt = request.StartAt ?? now.AddMinutes(-1);
        var endAt = request.EndAt ?? now.AddHours(2);
        if (endAt <= startAt)
        {
            return Result.Invalid("P12 production Pilot window endAt must be later than startAt.");
        }

        var window = new CloudReadonlyProductionPilotWindowDto(
            $"p12win_{ComputeHash($"{startAt:O}|{endAt:O}|{string.Join(",", endpoints)}")[..20]}",
            NormalizeText(request.Name, "P12 fixed-template production readonly Pilot", 120),
            CloudReadonlyProductionPilotWindowStatuses.PendingApproval,
            startAt,
            endAt,
            endpoints,
            Math.Clamp(request.MaxTimeRangeDays ?? options.MaxTimeRangeDays, 1, options.MaxTimeRangeDays),
            Math.Clamp(request.MaxRows ?? options.MaxRows, 1, options.MaxRows),
            Math.Clamp(request.TimeoutMs ?? options.TimeoutMs, 500, options.TimeoutMs),
            NormalizeText(request.OwnerDepartment, options.OwnerDepartment, 120),
            NormalizeText(request.ApprovalPolicy, options.ApprovalPolicy, 120),
            NormalizeText(request.RollbackPolicy, options.RollbackPolicy, 160));
        store.SaveWindow(window);

        return Result.Success(window);
    }

    public Result<CloudReadonlyProductionPilotWindowDto> UpdateWindowStatus(string windowId, string status)
    {
        var window = store.GetWindow(windowId);
        if (window is null)
        {
            return Result.NotFound();
        }

        var normalizedStatus = NormalizeWindowStatus(status);
        if (normalizedStatus is null)
        {
            return Result.Invalid("Unsupported P12 production Pilot window status.");
        }

        var updated = window with { Status = normalizedStatus };
        store.SaveWindow(updated);
        return Result.Success(updated);
    }

    public async Task<Result<CloudReadonlyProductionPilotScenarioResultDto>> RunScenarioAsync(
        RunCloudReadonlyProductionPilotScenarioCommand request,
        CloudReadonlyPilotReadinessStatusDto p11Status,
        IReadOnlyCollection<ToolRegistration>? persistedToolRegistrations,
        CancellationToken cancellationToken)
    {
        var status = BuildStatus(p11Status, persistedToolRegistrations);
        if (status.Status != CloudReadonlyProductionPilotStatuses.Ready)
        {
            return Result.Invalid($"CloudReadonlyProductionPilot is not ready. Status={status.Status}; blockers={string.Join("; ", status.Blockers)}");
        }

        var window = string.IsNullOrWhiteSpace(request.PilotWindowId)
            ? store.LatestWindow()
            : store.GetWindow(request.PilotWindowId);
        if (window is null)
        {
            return Result.Invalid("P12 production Pilot window is missing.");
        }

        if (!Scenarios.TryGetValue(request.ScenarioId?.Trim() ?? string.Empty, out var scenario))
        {
            return Result.Invalid("P12 production Pilot only allows fixed scenario ids.");
        }

        if (!pilotOptions.Value.AllowedScenarioIds.Contains(scenario.Id, StringComparer.OrdinalIgnoreCase))
        {
            return Result.Invalid("P12 production Pilot scenario is not allowed by configuration.");
        }

        if (!EndpointSpecs.TryGetValue(scenario.EndpointCode, out var endpoint) ||
            endpoint.IsBlockedByPolicy ||
            !window.AllowedEndpointCodes.Contains(scenario.EndpointCode, StringComparer.OrdinalIgnoreCase))
        {
            return Result.Invalid("P12 production Pilot endpoint is blocked by policy or not in the active window allowlist.");
        }

        var effectiveMaxRows = Math.Clamp(request.MaxRows <= 0 ? pilotOptions.Value.DefaultMaxRows : request.MaxRows, 1, window.MaxRows);
        var effectiveTimeoutMs = Math.Clamp(request.TimeoutMs <= 0 ? window.TimeoutMs : request.TimeoutMs, 500, window.TimeoutMs);
        var (from, to) = NormalizeTimeRange(request.TimeRange, window.MaxTimeRangeDays);
        if (to <= from)
        {
            return Result.Invalid("P12 production Pilot timeRange is invalid.");
        }

        if ((to - from).TotalDays > window.MaxTimeRangeDays)
        {
            return Result.Invalid("P12 production Pilot timeRange exceeds the active window maxTimeRange.");
        }

        var query = BuildQuery(scenario, window, effectiveMaxRows, from, to);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(effectiveTimeoutMs));
            using var document = await cloudAiReadClient.SendJsonAsync(
                endpoint.Method,
                endpoint.Path,
                query,
                timeoutCts.Token);
            stopwatch.Stop();

            var (rows, sourceTruncated) = ExtractRows(document.RootElement, effectiveMaxRows, endpoint.Code, window.WindowId);
            var resultHash = ComputeHash(JsonSerializer.Serialize(rows));
            var queryHash = ComputeHash($"{scenario.Id}|{window.WindowId}|{endpoint.Code}|{from:O}|{to:O}|{effectiveMaxRows}");
            var queryResult = new CloudProductionPilotQueryResultDto(
                endpoint.Code,
                CloudReadonlyProductionPilotMarkers.SourceType,
                CloudReadonlyProductionPilotMarkers.SourceMode,
                IsProductionData: true,
                IsSandbox: false,
                IsSimulation: false,
                CloudReadonlyProductionPilotMarkers.SourceLabel,
                CloudReadonlyProductionPilotMarkers.Boundary,
                window.WindowId,
                queryHash,
                resultHash,
                rows.Count,
                sourceTruncated,
                rows,
                DateTimeOffset.UtcNow,
                stopwatch.ElapsedMilliseconds,
                "ToolApprovalRequired");
            var result = new CloudReadonlyProductionPilotScenarioResultDto(
                scenario.Id,
                scenario.Title,
                CloudReadonlyProductionPilotStatuses.Completed,
                queryResult,
                NormalizeArtifactTypes(request.ArtifactTypes, scenario.ArtifactTypes));
            store.SaveRun(result);

            return Result.Success(result);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure(new ApiProblemDescriptor(
                CloudAiReadProblemCodes.Unavailable,
                "CloudReadonlyProductionPilot request timed out."));
        }
        catch (CloudAiReadException ex)
        {
            return Result.Failure(new ApiProblemDescriptor(
                ex.Code,
                $"CloudReadonlyProductionPilot query failed: {ex.Message}"));
        }
        catch (JsonException)
        {
            return Result.Failure(new ApiProblemDescriptor(
                CloudAiReadProblemCodes.Unavailable,
                "CloudReadonlyProductionPilot response shape is invalid JSON."));
        }
    }

    private void ValidateBoundary(
        ICollection<string> blockers,
        ICollection<string> warnings,
        CloudReadonlyPilotReadinessStatusDto p11Status,
        IReadOnlyCollection<ToolRegistration>? persistedToolRegistrations)
    {
        var cloudReadonly = cloudReadonlyOptions.Value;
        if (cloudReadonly.Mode != CloudReadonlyDataSourceMode.Disabled)
        {
            blockers.Add("CloudReadonly.Mode must remain Disabled during P12 fixed-template production Pilot.");
        }

        if (cloudReadonly.Real.Enabled)
        {
            blockers.Add("CloudReadonly.Real.Enabled must remain false by default during P12 fixed-template production Pilot.");
        }

        if (cloudReadonly.Real.AllowProductionRead)
        {
            blockers.Add("CloudReadonly.Real.AllowProductionRead must remain false by default during P12 fixed-template production Pilot.");
        }

        if (p11Status.Status != CloudReadonlyPilotReadinessStatuses.RehearsalPassed)
        {
            blockers.Add($"P11 readiness gate must be RehearsalPassed before P12 Pilot. Current={p11Status.Status}.");
        }

        ValidateProtectedTool(ProtectedCloudReadonlyToolPolicy.ProductionToolCode, persistedToolRegistrations, blockers, warnings);
        ValidateProtectedTool(ProtectedCloudReadonlyToolPolicy.PilotReadinessToolCode, persistedToolRegistrations, blockers, warnings);
        ValidateProtectedTool(ProtectedCloudReadonlyToolPolicy.ProductionPilotToolCode, persistedToolRegistrations, blockers, warnings);
    }

    private static void ValidateProtectedTool(
        string toolCode,
        IReadOnlyCollection<ToolRegistration>? persistedToolRegistrations,
        ICollection<string> blockers,
        ICollection<string> warnings)
    {
        var builtInTool = BuiltInToolRegistrations.AgentRuntimeTools.FirstOrDefault(
            item => string.Equals(item.ToolCode, toolCode, StringComparison.OrdinalIgnoreCase));
        if (builtInTool is null)
        {
            blockers.Add($"Tool Registry built-in definition is missing {toolCode}.");
        }
        else
        {
            var safety = ProtectedCloudReadonlyToolPolicy.ValidateSafeState(
                builtInTool.ToolCode,
                builtInTool.IsEnabled,
                builtInTool.IsVisibleToPlanner,
                builtInTool.IsExecutableByAgent,
                builtInTool.ApprovalPolicy);
            if (safety is not null)
            {
                blockers.Add($"Built-in ToolRegistry unsafe: {safety}");
            }
        }

        if (persistedToolRegistrations is null)
        {
            warnings.Add("P12 Pilot did not receive persisted ToolRegistry state; only built-in definitions were checked.");
            return;
        }

        var persisted = persistedToolRegistrations.FirstOrDefault(
            item => string.Equals(item.ToolCode, toolCode, StringComparison.OrdinalIgnoreCase));
        if (persisted is null)
        {
            blockers.Add($"Persisted ToolRegistry is missing protected tool {toolCode}.");
            return;
        }

        var persistedSafety = ProtectedCloudReadonlyToolPolicy.ValidateSafeState(
            persisted.ToolCode,
            persisted.IsEnabled,
            persisted.IsVisibleToPlanner,
            persisted.IsExecutableByAgent,
            persisted.ApprovalPolicy);
        if (persistedSafety is not null)
        {
            blockers.Add($"Persisted ToolRegistry unsafe: {persistedSafety}");
        }

        if (string.Equals(toolCode, ProtectedCloudReadonlyToolPolicy.ProductionPilotToolCode, StringComparison.OrdinalIgnoreCase) &&
            persisted.DataBoundary != ToolDataBoundary.CloudReadonlyProductionPilotOnly)
        {
            blockers.Add($"{toolCode} must use CloudReadonlyProductionPilotOnly data boundary.");
        }
    }

    private static string ResolveWindowStatus(
        CloudReadonlyProductionPilotWindowDto window,
        DateTimeOffset now,
        ICollection<string> blockers)
    {
        return window.Status switch
        {
            CloudReadonlyProductionPilotWindowStatuses.PendingApproval => CloudReadonlyProductionPilotStatuses.WindowPendingApproval,
            CloudReadonlyProductionPilotWindowStatuses.Paused => CloudReadonlyProductionPilotStatuses.Paused,
            CloudReadonlyProductionPilotWindowStatuses.Completed => CloudReadonlyProductionPilotStatuses.Expired,
            CloudReadonlyProductionPilotWindowStatuses.EmergencyStopped => CloudReadonlyProductionPilotStatuses.EmergencyStopped,
            CloudReadonlyProductionPilotWindowStatuses.Approved when now < window.StartAt => CloudReadonlyProductionPilotStatuses.WindowNotStarted,
            CloudReadonlyProductionPilotWindowStatuses.Approved when now > window.EndAt => CloudReadonlyProductionPilotStatuses.Expired,
            CloudReadonlyProductionPilotWindowStatuses.Approved => CloudReadonlyProductionPilotStatuses.Ready,
            _ => AddUnknownWindowStatus(blockers, window.Status)
        };
    }

    private static string AddUnknownWindowStatus(ICollection<string> blockers, string status)
    {
        blockers.Add($"Unknown P12 production Pilot window status: {status}.");
        return CloudReadonlyProductionPilotStatuses.Blocked;
    }

    private static string? NormalizeWindowStatus(string? status)
    {
        var value = status?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return new[]
            {
                CloudReadonlyProductionPilotWindowStatuses.PendingApproval,
                CloudReadonlyProductionPilotWindowStatuses.Approved,
                CloudReadonlyProductionPilotWindowStatuses.Paused,
                CloudReadonlyProductionPilotWindowStatuses.Completed,
                CloudReadonlyProductionPilotWindowStatuses.EmergencyStopped
            }
            .FirstOrDefault(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] NormalizeEndpointCodes(IEnumerable<string> endpointCodes)
    {
        return endpointCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Where(code =>
                EndpointSpecs.TryGetValue(code, out var spec) &&
                !spec.IsBlockedByPolicy &&
                CloudAiReadEndpointPolicy.IsSafeRouteSegment(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string?> BuildQuery(
        ProductionPilotScenario scenario,
        CloudReadonlyProductionPilotWindowDto window,
        int maxRows,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        return new Dictionary<string, string?>
        {
            ["scenarioId"] = scenario.Id,
            ["maxRows"] = maxRows.ToString(),
            ["from"] = from.ToString("O"),
            ["to"] = to.ToString("O"),
            ["boundary"] = CloudReadonlyProductionPilotMarkers.Boundary,
            ["pilotWindowId"] = window.WindowId
        };
    }

    private static (DateTimeOffset From, DateTimeOffset To) NormalizeTimeRange(
        CloudProductionPilotTimeRangeDto? timeRange,
        int maxTimeRangeDays)
    {
        var to = timeRange?.To ?? DateTimeOffset.UtcNow;
        var from = timeRange?.From ?? to.AddDays(-Math.Min(maxTimeRangeDays, 1));
        return (from, to);
    }

    private static IReadOnlyCollection<string> NormalizeArtifactTypes(
        IReadOnlyCollection<string>? requested,
        IReadOnlyCollection<string> defaults)
    {
        var allowed = new HashSet<string>(pilotArtifactTypes, StringComparer.OrdinalIgnoreCase);
        var values = requested is { Count: > 0 } ? requested : defaults;
        return values
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Where(item => allowed.Contains(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static readonly string[] pilotArtifactTypes = ["Chart", "Markdown", "Html", "Pdf", "Pptx", "Xlsx"];

    private static (IReadOnlyCollection<IReadOnlyDictionary<string, object?>> Rows, bool IsTruncated) ExtractRows(
        JsonElement root,
        int maxRows,
        string endpointCode,
        string windowId)
    {
        var sourceRows = EnumerateRows(root).ToArray();
        var isTruncated = ReadIsTruncated(root) || sourceRows.Length > maxRows;
        var rows = sourceRows
            .Take(maxRows)
            .Select(row =>
            {
                var normalized = new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase)
                {
                    ["sourceType"] = CloudReadonlyProductionPilotMarkers.SourceType,
                    ["sourceMode"] = CloudReadonlyProductionPilotMarkers.SourceMode,
                    ["isProductionData"] = true,
                    ["isSandbox"] = false,
                    ["isSimulation"] = false,
                    ["sourceLabel"] = CloudReadonlyProductionPilotMarkers.SourceLabel,
                    ["boundary"] = CloudReadonlyProductionPilotMarkers.Boundary,
                    ["pilotWindowId"] = windowId,
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

    private static string NormalizeText(string? value, string fallback, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return normalized.Length > maxLength ? normalized[..maxLength] : normalized;
    }

    private static string ComputeHash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private sealed record ProductionPilotScenario(
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
