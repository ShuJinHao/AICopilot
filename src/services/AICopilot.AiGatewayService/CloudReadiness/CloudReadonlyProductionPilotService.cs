using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Options;

namespace AICopilot.AiGatewayService.CloudReadiness;

public sealed class CloudReadonlyProductionPilotService(
    IOptions<CloudReadonlyOptions> cloudReadonlyOptions,
    IOptions<CloudAiReadOptions> cloudAiReadOptions,
    IOptions<CloudReadonlyProductionPilotOptions> pilotOptions,
    ICloudReadonlyProductionPilotStore store,
    ICloudAiReadClient cloudAiReadClient,
    IProductionPilotOperationsStore? operationsStore = null)
{
    public static IReadOnlyCollection<string> ScenarioIds => CloudReadonlyProductionPilotScenarioCatalog.ScenarioIds;

    public static bool IsScenarioId(string? scenarioId) =>
        CloudReadonlyProductionPilotScenarioCatalog.IsScenarioId(scenarioId);

    public static string? ResolveScenarioTitle(string? scenarioId) =>
        CloudReadonlyProductionPilotScenarioCatalog.ResolveScenarioTitle(scenarioId);

    public static string? ResolveScenarioDomain(string? scenarioId) =>
        CloudReadonlyProductionPilotScenarioCatalog.ResolveScenarioDomain(scenarioId);

    public static IReadOnlyCollection<string> ResolveScenarioArtifactTypes(string? scenarioId) =>
        CloudReadonlyProductionPilotScenarioCatalog.ResolveScenarioArtifactTypes(scenarioId);

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
        var emergencyStop = operationsStore?.GetEmergencyStop();

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

        if (emergencyStop?.IsActive == true)
        {
            blockers.Add("Production Pilot emergency stop is active; P12 fixed-template Pilot tools are blocked.");
            return new CloudReadonlyProductionPilotStatusDto(
                CloudReadonlyProductionPilotStatuses.EmergencyStopped,
                Enabled: true,
                window?.WindowId,
                window?.Status,
                window?.AllowedEndpointCodes ?? options.AllowedEndpointCodes,
                window?.Status == CloudReadonlyProductionPilotWindowStatuses.Approved ? "Approved" : "Required",
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

        var endpoints = CloudReadonlyProductionPilotScenarioCatalog.NormalizeEndpointCodes(
            request.AllowedEndpointCodes ?? options.AllowedEndpointCodes);
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

        if (!CloudReadonlyProductionPilotScenarioCatalog.TryGetScenario(request.ScenarioId, out var scenario))
        {
            return Result.Invalid("P12 production Pilot only allows fixed scenario ids.");
        }

        if (!pilotOptions.Value.AllowedScenarioIds.Contains(scenario.Id, StringComparer.OrdinalIgnoreCase))
        {
            return Result.Invalid("P12 production Pilot scenario is not allowed by configuration.");
        }

        if (!CloudReadonlyProductionPilotScenarioCatalog.TryGetEndpoint(scenario.EndpointCode, out var endpoint) ||
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

        var query = CloudReadonlyProductionPilotScenarioCatalog.BuildQuery(scenario, window, effectiveMaxRows, from, to);
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

            var (rows, sourceTruncated) = CloudReadonlyProductionPilotScenarioCatalog.ExtractRows(
                document.RootElement,
                effectiveMaxRows,
                endpoint.Code,
                window.WindowId);
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
                CloudReadonlyProductionPilotScenarioCatalog.NormalizeArtifactTypes(request.ArtifactTypes, scenario.ArtifactTypes));
            store.SaveRun(result);
            operationsStore?.UpsertRunLedger(
                CloudReadonlyProductionOperationsService.CreateRunLedger(result),
                DateTimeOffset.UtcNow);

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

    private static (DateTimeOffset From, DateTimeOffset To) NormalizeTimeRange(
        CloudProductionPilotTimeRangeDto? timeRange,
        int maxTimeRangeDays)
    {
        var to = timeRange?.To ?? DateTimeOffset.UtcNow;
        var from = timeRange?.From ?? to.AddDays(-Math.Min(maxTimeRangeDays, 1));
        return (from, to);
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

}
