using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Options;

namespace AICopilot.AiGatewayService.CloudReadiness;

public sealed class CloudReadonlyProductionControlledPilotService(
    IOptions<CloudReadonlyOptions> cloudReadonlyOptions,
    IOptions<CloudAiReadOptions> cloudAiReadOptions,
    IOptions<CloudReadonlyProductionControlledPilotOptions> controlledOptions,
    ICloudReadonlyProductionControlledPilotStore store,
    ICloudAiReadClient cloudAiReadClient,
    IProductionPilotOperationsStore? operationsStore = null)
{
    public CloudReadonlyProductionControlledPilotStatusDto BuildStatus(
        CloudReadonlyProductionPilotStatusDto p12Status,
        IReadOnlyCollection<ToolRegistration>? persistedToolRegistrations = null)
    {
        var options = controlledOptions.Value;
        var latestRun = store.ListRuns().FirstOrDefault();
        var blockers = new List<string>();
        var warnings = new List<string>();

        ValidateBoundary(blockers, warnings, p12Status, persistedToolRegistrations);
        var emergencyStop = operationsStore?.GetEmergencyStop();

        if (!options.Enabled)
        {
            return new CloudReadonlyProductionControlledPilotStatusDto(
                CloudReadonlyProductionControlledPilotStatuses.Disabled,
                Enabled: false,
                p12Status.Status,
                p12Status.PilotWindowId,
                p12Status.WindowStatus,
                options.FreeGoalEnabled,
                p12Status.AllowedEndpointCodes,
                ToolVisible: false,
                ToolExecutable: false,
                latestRun?.QueryResult.ExecutedAt,
                blockers,
                warnings);
        }

        if (emergencyStop?.IsActive == true)
        {
            blockers.Add("Production Pilot emergency stop is active; P13 controlled Pilot tools are blocked.");
            return new CloudReadonlyProductionControlledPilotStatusDto(
                CloudReadonlyProductionControlledPilotStatuses.EmergencyStopped,
                Enabled: true,
                p12Status.Status,
                p12Status.PilotWindowId,
                p12Status.WindowStatus,
                options.FreeGoalEnabled,
                p12Status.AllowedEndpointCodes,
                ToolVisible: false,
                ToolExecutable: false,
                latestRun?.QueryResult.ExecutedAt,
                blockers,
                warnings);
        }

        if (!options.FreeGoalEnabled)
        {
            return new CloudReadonlyProductionControlledPilotStatusDto(
                CloudReadonlyProductionControlledPilotStatuses.FreeGoalDisabled,
                Enabled: true,
                p12Status.Status,
                p12Status.PilotWindowId,
                p12Status.WindowStatus,
                FreeGoalEnabled: false,
                p12Status.AllowedEndpointCodes,
                ToolVisible: false,
                ToolExecutable: false,
                latestRun?.QueryResult.ExecutedAt,
                blockers,
                warnings);
        }

        if (p12Status.Status != CloudReadonlyProductionPilotStatuses.Ready)
        {
            blockers.Add($"P12 production Pilot gate must be Ready before P13 controlled Pilot. Current={p12Status.Status}.");
        }

        if (!cloudAiReadClient.IsEnabled || !cloudAiReadOptions.Value.IsConfigured())
        {
            blockers.Add("CloudAiRead must be configured before P13 production controlled Pilot can execute.");
        }

        var allowedEndpoints = CloudReadonlyProductionControlledPilotGoalPolicy
            .FilterAllowedEndpointCodes(options.AllowedEndpointCodes)
            .Where(code => p12Status.AllowedEndpointCodes.Contains(code, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (allowedEndpoints.Length == 0)
        {
            blockers.Add("CloudReadonlyProductionControlledPilot has no endpoint allowed by both P13 config and the active P12 Pilot Window.");
        }

        var ready = blockers.Count == 0;
        return new CloudReadonlyProductionControlledPilotStatusDto(
            ready ? CloudReadonlyProductionControlledPilotStatuses.Ready : CloudReadonlyProductionControlledPilotStatuses.Blocked,
            Enabled: true,
            p12Status.Status,
            p12Status.PilotWindowId,
            p12Status.WindowStatus,
            FreeGoalEnabled: true,
            allowedEndpoints,
            ToolVisible: ready,
            ToolExecutable: ready,
            latestRun?.QueryResult.ExecutedAt,
            blockers,
            warnings);
    }

    public Result<CloudProductionGoalIntentDto> CreateIntent(
        string goal,
        IReadOnlyCollection<string>? artifactTypes,
        CloudProductionGoalTimeRangeDto? timeRange,
        int? maxRows,
        CloudReadonlyProductionPilotStatusDto p12Status,
        IReadOnlyCollection<ToolRegistration>? persistedToolRegistrations = null)
    {
        var status = BuildStatus(p12Status, persistedToolRegistrations);
        if (status.Status != CloudReadonlyProductionControlledPilotStatuses.Ready)
        {
            return Result.Invalid($"CloudReadonlyProductionControlledPilot is not ready. Status={status.Status}; P12={status.P12GateStatus}; blockers={string.Join("; ", status.Blockers)}");
        }

        var options = controlledOptions.Value;
        var warnings = new List<string>();
        var rejected = new List<string>();
        var normalizedGoal = CloudReadonlyProductionControlledPilotGoalPolicy.NormalizeGoal(goal);
        if (string.IsNullOrWhiteSpace(normalizedGoal))
        {
            rejected.Add("Goal is required.");
        }

        if (CloudReadonlyProductionControlledPilotGoalPolicy.ContainsBlockedGoalTerm(normalizedGoal))
        {
            rejected.Add("BlockedByPolicy: controlled production goal cannot request Recipe, write paths, arbitrary payloads, SQL, or Cloud write semantics.");
        }

        var endpointCode = CloudReadonlyProductionControlledPilotGoalPolicy.ResolveEndpointCode(normalizedGoal);
        var allowedEndpoints = status.AllowedEndpointCodes;
        if (endpointCode is null)
        {
            rejected.Add("BlockedByPolicy: controlled production goal could not be mapped to an allowed endpoint.");
        }
        else if (!allowedEndpoints.Contains(endpointCode, StringComparer.OrdinalIgnoreCase))
        {
            rejected.Add($"BlockedByPolicy: endpoint '{endpointCode}' is not in the active production controlled allowlist.");
        }

        var normalizedArtifactTypes = CloudReadonlyProductionControlledPilotGoalPolicy.NormalizeArtifactTypes(
            artifactTypes,
            options,
            rejected);
        var normalizedTimeRange = CloudReadonlyProductionControlledPilotGoalPolicy.NormalizeTimeRange(
            timeRange,
            options,
            rejected,
            warnings);
        var effectiveMaxRows = maxRows ?? options.DefaultMaxRows;
        if (effectiveMaxRows < 1 || effectiveMaxRows > options.MaxRows)
        {
            rejected.Add($"maxRows must be between 1 and {options.MaxRows}.");
        }

        var intent = new CloudProductionGoalIntentDto(
            $"pcg_{ComputeHash($"{normalizedGoal}|{DateTimeOffset.UtcNow:O}")[..20]}",
            ComputeHash(normalizedGoal),
            endpointCode is null ? [] : [endpointCode],
            normalizedTimeRange,
            effectiveMaxRows,
            normalizedArtifactTypes,
            CloudReadonlyProductionControlledPilotGoalPolicy.ResolveAnalysisType(normalizedGoal, endpointCode),
            warnings,
            rejected,
            options.RequiresToolApproval,
            options.RequiresFinalApproval);

        if (rejected.Count > 0)
        {
            return Result.Invalid(string.Join("; ", rejected));
        }

        store.SaveIntent(intent);
        return Result.Success(intent);
    }

    public Result ValidateIntentForPlan(
        CloudProductionGoalIntentDto? intent,
        CloudReadonlyProductionPilotStatusDto p12Status,
        IReadOnlyCollection<ToolRegistration>? persistedToolRegistrations = null)
    {
        return ValidateIntent(intent, requireIntentStoreEntry: true, p12Status, persistedToolRegistrations);
    }

    public Task<Result<CloudReadonlyProductionControlledPilotResultDto>> RunIntentAsync(
        string intentId,
        IReadOnlyCollection<string>? artifactTypes,
        int maxRows,
        int timeoutMs,
        CloudReadonlyProductionPilotStatusDto p12Status,
        IReadOnlyCollection<ToolRegistration>? persistedToolRegistrations,
        CancellationToken cancellationToken)
    {
        var intent = store.GetIntent(intentId);
        return intent is null
            ? Task.FromResult((Result<CloudReadonlyProductionControlledPilotResultDto>)Result.Invalid("CloudProductionGoalIntent was not found or has expired."))
            : RunIntentAsync(intent, artifactTypes, maxRows, timeoutMs, p12Status, persistedToolRegistrations, cancellationToken);
    }

    public async Task<Result<CloudReadonlyProductionControlledPilotResultDto>> RunIntentAsync(
        CloudProductionGoalIntentDto intent,
        IReadOnlyCollection<string>? artifactTypes,
        int maxRows,
        int timeoutMs,
        CloudReadonlyProductionPilotStatusDto p12Status,
        IReadOnlyCollection<ToolRegistration>? persistedToolRegistrations,
        CancellationToken cancellationToken)
    {
        var validation = ValidateIntent(intent, requireIntentStoreEntry: false, p12Status, persistedToolRegistrations);
        if (!validation.IsSuccess)
        {
            return Result.From(validation);
        }

        var endpointCode = intent.EndpointCodes.First();
        if (!CloudReadonlyProductionControlledPilotGoalPolicy.TryGetEndpoint(endpointCode, out var endpoint) ||
            endpoint.IsBlockedByPolicy)
        {
            return Result.Invalid("CloudReadonlyProductionControlledPilot endpoint is blocked by policy.");
        }

        var options = controlledOptions.Value;
        var effectiveMaxRows = Math.Clamp(maxRows <= 0 ? intent.MaxRows : maxRows, 1, options.MaxRows);
        var effectiveTimeoutMs = Math.Clamp(timeoutMs <= 0 ? options.TimeoutMs : timeoutMs, 500, options.TimeoutMs);
        var pilotWindowId = p12Status.PilotWindowId ?? "none";
        var query = CloudReadonlyProductionControlledPilotQueryProjection.BuildQuery(
            intent,
            pilotWindowId,
            effectiveMaxRows);
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

            var (rows, sourceTruncated) = CloudReadonlyProductionControlledPilotQueryProjection.ExtractRows(
                document.RootElement,
                effectiveMaxRows,
                endpoint.Code,
                pilotWindowId,
                intent.IntentId);
            var resultHash = ComputeHash(JsonSerializer.Serialize(rows));
            var queryHash = ComputeHash($"{intent.IntentId}|{intent.GoalHash}|{endpoint.Code}|{effectiveMaxRows}|{CloudReadonlyProductionControlledPilotMarkers.Boundary}");
            var queryResult = new CloudProductionControlledQueryResultDto(
                endpoint.Code,
                CloudReadonlyProductionControlledPilotMarkers.SourceType,
                CloudReadonlyProductionControlledPilotMarkers.SourceMode,
                IsProductionData: true,
                IsSandbox: false,
                IsSimulation: false,
                CloudReadonlyProductionControlledPilotMarkers.SourceLabel,
                CloudReadonlyProductionControlledPilotMarkers.Boundary,
                pilotWindowId,
                intent.IntentId,
                queryHash,
                resultHash,
                rows.Count,
                sourceTruncated,
                rows,
                DateTimeOffset.UtcNow,
                stopwatch.ElapsedMilliseconds,
                "ToolApprovalRequired");
            var artifactRejected = new List<string>();
            var result = new CloudReadonlyProductionControlledPilotResultDto(
                intent.IntentId,
                intent.AnalysisType,
                CloudReadonlyProductionControlledPilotStatuses.Completed,
                queryResult,
                CloudReadonlyProductionControlledPilotGoalPolicy.NormalizeArtifactTypes(artifactTypes, options, artifactRejected));
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
                "CloudReadonlyProductionControlledPilot request timed out."));
        }
        catch (CloudAiReadException ex)
        {
            return Result.Failure(new ApiProblemDescriptor(
                ex.Code,
                $"CloudReadonlyProductionControlledPilot query failed: {ex.Message}"));
        }
        catch (JsonException)
        {
            return Result.Failure(new ApiProblemDescriptor(
                CloudAiReadProblemCodes.Unavailable,
                "CloudReadonlyProductionControlledPilot response shape is invalid JSON."));
        }
    }

    private Result ValidateIntent(
        CloudProductionGoalIntentDto? intent,
        bool requireIntentStoreEntry,
        CloudReadonlyProductionPilotStatusDto p12Status,
        IReadOnlyCollection<ToolRegistration>? persistedToolRegistrations)
    {
        var status = BuildStatus(p12Status, persistedToolRegistrations);
        if (status.Status != CloudReadonlyProductionControlledPilotStatuses.Ready)
        {
            return Result.Invalid($"CloudReadonlyProductionControlledPilot is not ready. Status={status.Status}.");
        }

        if (intent is null || string.IsNullOrWhiteSpace(intent.IntentId))
        {
            return Result.Invalid("CloudProductionGoalIntent is required for controlled production Pilot plans.");
        }

        var stored = store.GetIntent(intent.IntentId);
        if (requireIntentStoreEntry &&
            (stored is null || !string.Equals(stored.GoalHash, intent.GoalHash, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Invalid("CloudProductionGoalIntent was not created by the controlled production intent gate.");
        }

        if (intent.RejectedReasons.Count > 0)
        {
            return Result.Invalid("CloudProductionGoalIntent contains rejected reasons and cannot be planned.");
        }

        if (intent.EndpointCodes.Count == 0 ||
            intent.EndpointCodes.Any(code => !status.AllowedEndpointCodes.Contains(code, StringComparer.OrdinalIgnoreCase)))
        {
            return Result.Invalid("CloudProductionGoalIntent endpoint is outside the controlled production allowlist.");
        }

        if (intent.MaxRows < 1 || intent.MaxRows > controlledOptions.Value.MaxRows)
        {
            return Result.Invalid("CloudProductionGoalIntent maxRows is outside the controlled production limit.");
        }

        return Result.Success();
    }

    private void ValidateBoundary(
        ICollection<string> blockers,
        ICollection<string> warnings,
        CloudReadonlyProductionPilotStatusDto p12Status,
        IReadOnlyCollection<ToolRegistration>? persistedToolRegistrations)
    {
        var cloudReadonly = cloudReadonlyOptions.Value;
        if (cloudReadonly.Mode != CloudReadonlyDataSourceMode.Disabled)
        {
            blockers.Add("CloudReadonly.Mode must remain Disabled during P13 controlled production Pilot.");
        }

        if (cloudReadonly.Real.Enabled)
        {
            blockers.Add("CloudReadonly.Real.Enabled must remain false by default during P13 controlled production Pilot.");
        }

        if (cloudReadonly.Real.AllowProductionRead)
        {
            blockers.Add("CloudReadonly.Real.AllowProductionRead must remain false by default during P13 controlled production Pilot.");
        }

        if (p12Status.Status == CloudReadonlyProductionPilotStatuses.Blocked)
        {
            blockers.Add("P12 production Pilot gate is blocked.");
        }

        ValidateProtectedTool(ProtectedCloudReadonlyToolPolicy.ProductionToolCode, persistedToolRegistrations, blockers, warnings);
        ValidateProtectedTool(ProtectedCloudReadonlyToolPolicy.PilotReadinessToolCode, persistedToolRegistrations, blockers, warnings);
        ValidateProtectedTool(ProtectedCloudReadonlyToolPolicy.ProductionPilotToolCode, persistedToolRegistrations, blockers, warnings);
        ValidateProtectedTool(ProtectedCloudReadonlyToolPolicy.ProductionControlledToolCode, persistedToolRegistrations, blockers, warnings);
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
            warnings.Add("P13 controlled Pilot did not receive persisted ToolRegistry state; only built-in definitions were checked.");
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

        if (string.Equals(toolCode, ProtectedCloudReadonlyToolPolicy.ProductionControlledToolCode, StringComparison.OrdinalIgnoreCase) &&
            persisted.DataBoundary != ToolDataBoundary.CloudReadonlyProductionControlledOnly)
        {
            blockers.Add($"{toolCode} must use CloudReadonlyProductionControlledOnly data boundary.");
        }
    }

    private static string ComputeHash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
