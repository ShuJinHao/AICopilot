using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.TrialOperations;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using MediatR;
using Microsoft.Extensions.Options;

namespace AICopilot.AiGatewayService.CloudReadiness;

public static class CloudReadonlyProductionControlledPilotStatuses
{
    public const string Disabled = "Disabled";
    public const string FreeGoalDisabled = "FreeGoalDisabled";
    public const string P12GateRequired = "P12GateRequired";
    public const string Ready = "Ready";
    public const string EmergencyStopped = "EmergencyStopped";
    public const string Blocked = "Blocked";
    public const string Failed = "Failed";
    public const string Completed = "Completed";
}

public sealed record CloudReadonlyProductionControlledPilotStatusDto(
    string Status,
    bool Enabled,
    string P12GateStatus,
    string? PilotWindowId,
    string? WindowStatus,
    bool FreeGoalEnabled,
    IReadOnlyCollection<string> AllowedEndpointCodes,
    bool ToolVisible,
    bool ToolExecutable,
    DateTimeOffset? LastRunAt,
    IReadOnlyCollection<string> Blockers,
    IReadOnlyCollection<string> Warnings,
    string Boundary = CloudReadonlyProductionControlledPilotMarkers.Boundary);

public sealed record CloudProductionGoalTimeRangeDto(DateTimeOffset? From = null, DateTimeOffset? To = null);

public sealed record CloudProductionGoalIntentDto(
    string IntentId,
    string GoalHash,
    IReadOnlyCollection<string> EndpointCodes,
    CloudProductionGoalTimeRangeDto TimeRange,
    int MaxRows,
    IReadOnlyCollection<string> ArtifactTypes,
    string AnalysisType,
    IReadOnlyCollection<string> Warnings,
    IReadOnlyCollection<string> RejectedReasons,
    bool RequiresToolApproval,
    bool RequiresFinalApproval);

public sealed record CloudReadonlyProductionControlledPlanDto(
    AgentTaskDto Task,
    CloudProductionGoalIntentDto Intent);

public sealed record CloudProductionControlledQueryResultDto(
    string EndpointCode,
    string SourceType,
    string SourceMode,
    bool IsProductionData,
    bool IsSandbox,
    bool IsSimulation,
    string SourceLabel,
    string Boundary,
    string PilotWindowId,
    string IntentId,
    string QueryHash,
    string ResultHash,
    int RowCount,
    bool IsTruncated,
    IReadOnlyCollection<IReadOnlyDictionary<string, object?>> Rows,
    DateTimeOffset ExecutedAt,
    long DurationMs,
    string ApprovalStatus);

public sealed record CloudReadonlyProductionControlledPilotResultDto(
    string IntentId,
    string AnalysisType,
    string Status,
    CloudProductionControlledQueryResultDto QueryResult,
    IReadOnlyCollection<string> ArtifactTypes,
    string Boundary = CloudReadonlyProductionControlledPilotMarkers.Boundary);

[AuthorizeRequirement(TrialOperationsPermissions.Read)]
public sealed record GetCloudReadonlyProductionControlledPilotStatusQuery
    : IQuery<Result<CloudReadonlyProductionControlledPilotStatusDto>>;

[AuthorizeRequirement("AiGateway.PlanAgentTask")]
public sealed record CreateCloudReadonlyProductionControlledPlanCommand(
    Guid SessionId,
    string Goal,
    Guid? ModelId = null,
    IReadOnlyCollection<string>? ArtifactTypes = null,
    CloudProductionGoalTimeRangeDto? TimeRange = null,
    int? MaxRows = null,
    string? PlannerMode = null) : ICommand<Result<CloudReadonlyProductionControlledPlanDto>>;

[AuthorizeRequirement("AiGateway.RunAgentTask")]
public sealed record RunCloudReadonlyProductionControlledPilotCommand(
    string IntentId,
    IReadOnlyCollection<string>? ArtifactTypes = null,
    int MaxRows = 20,
    int TimeoutMs = 5000) : ICommand<Result<CloudReadonlyProductionControlledPilotResultDto>>;

public sealed class GetCloudReadonlyProductionControlledPilotStatusQueryHandler(
    CloudReadonlyProductionControlledPilotService controlledPilotService,
    CloudReadonlyProductionPilotService productionPilotService,
    CloudReadonlyPilotReadinessService pilotReadinessService,
    IReadRepository<ToolRegistration> toolRepository)
    : IQueryHandler<GetCloudReadonlyProductionControlledPilotStatusQuery, Result<CloudReadonlyProductionControlledPilotStatusDto>>
{
    public async Task<Result<CloudReadonlyProductionControlledPilotStatusDto>> Handle(
        GetCloudReadonlyProductionControlledPilotStatusQuery request,
        CancellationToken cancellationToken)
    {
        var protectedTools = await GetCloudReadonlyPilotReadinessQueryHandler.LoadProtectedToolRegistrationsAsync(
            toolRepository,
            cancellationToken);
        var p12Status = productionPilotService.BuildStatus(
            pilotReadinessService.BuildStatus(protectedTools),
            protectedTools);
        return Result.Success(controlledPilotService.BuildStatus(p12Status, protectedTools));
    }
}

public sealed class CreateCloudReadonlyProductionControlledPlanCommandHandler(
    CloudReadonlyProductionControlledPilotService controlledPilotService,
    CloudReadonlyProductionPilotService productionPilotService,
    CloudReadonlyPilotReadinessService pilotReadinessService,
    IReadRepository<ToolRegistration> toolRepository,
    ISender sender)
    : ICommandHandler<CreateCloudReadonlyProductionControlledPlanCommand, Result<CloudReadonlyProductionControlledPlanDto>>
{
    public async Task<Result<CloudReadonlyProductionControlledPlanDto>> Handle(
        CreateCloudReadonlyProductionControlledPlanCommand request,
        CancellationToken cancellationToken)
    {
        var protectedTools = await GetCloudReadonlyPilotReadinessQueryHandler.LoadProtectedToolRegistrationsAsync(
            toolRepository,
            cancellationToken);
        var p12Status = productionPilotService.BuildStatus(
            pilotReadinessService.BuildStatus(protectedTools),
            protectedTools);
        var intentResult = controlledPilotService.CreateIntent(
            request.Goal,
            request.ArtifactTypes,
            request.TimeRange,
            request.MaxRows,
            p12Status,
            protectedTools);
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
                QueryMode: CloudReadonlyProductionControlledPilotMarkers.SourceMode,
                RequiresDataApproval: true,
                PlannerMode: request.PlannerMode ?? "StaticOnly",
                IsCloudProductionControlledPilotTrial: true,
                CloudProductionGoalIntent: intentResult.Value),
            cancellationToken);
        if (!taskResult.IsSuccess || taskResult.Value is null)
        {
            return Result.From(taskResult);
        }

        return Result.Success(new CloudReadonlyProductionControlledPlanDto(taskResult.Value, intentResult.Value));
    }
}

public sealed class RunCloudReadonlyProductionControlledPilotCommandHandler(
    CloudReadonlyProductionControlledPilotService controlledPilotService,
    CloudReadonlyProductionPilotService productionPilotService,
    CloudReadonlyPilotReadinessService pilotReadinessService,
    IReadRepository<ToolRegistration> toolRepository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<RunCloudReadonlyProductionControlledPilotCommand, Result<CloudReadonlyProductionControlledPilotResultDto>>
{
    public async Task<Result<CloudReadonlyProductionControlledPilotResultDto>> Handle(
        RunCloudReadonlyProductionControlledPilotCommand request,
        CancellationToken cancellationToken)
    {
        var protectedTools = await GetCloudReadonlyPilotReadinessQueryHandler.LoadProtectedToolRegistrationsAsync(
            toolRepository,
            cancellationToken);
        var p12Status = productionPilotService.BuildStatus(
            pilotReadinessService.BuildStatus(protectedTools),
            protectedTools);
        var result = await controlledPilotService.RunIntentAsync(
            request.IntentId,
            request.ArtifactTypes,
            request.MaxRows,
            request.TimeoutMs,
            p12Status,
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
                "AiGateway.RunCloudReadonlyProductionControlledPilot",
                "CloudReadonlyProductionControlledPilot",
                query.IntentId,
                result.Value.Status,
                AuditResults.Succeeded,
                $"Ran P13 production controlled readonly Pilot; endpoint={query.EndpointCode}; rows={query.RowCount}; truncated={query.IsTruncated}; resultHash={query.ResultHash}.",
                ["intentId", "endpointCode", "resultHash", "rowCount", "isTruncated"]),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        return result;
    }
}

public interface ICloudReadonlyProductionControlledPilotStore
{
    void SaveIntent(CloudProductionGoalIntentDto intent);

    CloudProductionGoalIntentDto? GetIntent(string intentId);

    void SaveRun(CloudReadonlyProductionControlledPilotResultDto result);

    IReadOnlyCollection<CloudReadonlyProductionControlledPilotResultDto> ListRuns();
}

internal sealed class InMemoryCloudReadonlyProductionControlledPilotStore
    : ICloudReadonlyProductionControlledPilotStore
{
    private readonly object sync = new();
    private readonly Dictionary<string, CloudProductionGoalIntentDto> intents = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CloudReadonlyProductionControlledPilotResultDto> runs = [];

    public void SaveIntent(CloudProductionGoalIntentDto intent)
    {
        lock (sync)
        {
            intents[intent.IntentId] = intent;
            foreach (var key in intents.Keys.Take(Math.Max(0, intents.Count - 100)).ToArray())
            {
                intents.Remove(key);
            }
        }
    }

    public CloudProductionGoalIntentDto? GetIntent(string intentId)
    {
        lock (sync)
        {
            return string.IsNullOrWhiteSpace(intentId) ? null : intents.GetValueOrDefault(intentId);
        }
    }

    public void SaveRun(CloudReadonlyProductionControlledPilotResultDto result)
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

    public IReadOnlyCollection<CloudReadonlyProductionControlledPilotResultDto> ListRuns()
    {
        lock (sync)
        {
            return runs.ToArray();
        }
    }
}

public sealed class CloudReadonlyProductionControlledPilotService(
    IOptions<CloudReadonlyOptions> cloudReadonlyOptions,
    IOptions<CloudAiReadOptions> cloudAiReadOptions,
    IOptions<CloudReadonlyProductionControlledPilotOptions> controlledOptions,
    ICloudReadonlyProductionControlledPilotStore store,
    ICloudAiReadClient cloudAiReadClient,
    IProductionPilotOperationsStore? operationsStore = null)
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

        var allowedEndpoints = FilterAllowedEndpointCodes(options.AllowedEndpointCodes)
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
        var normalizedGoal = NormalizeGoal(goal);
        if (string.IsNullOrWhiteSpace(normalizedGoal))
        {
            rejected.Add("Goal is required.");
        }

        if (ContainsBlockedGoalTerm(normalizedGoal))
        {
            rejected.Add("BlockedByPolicy: controlled production goal cannot request Recipe, write paths, arbitrary payloads, SQL, or Cloud write semantics.");
        }

        var endpointCode = ResolveEndpointCode(normalizedGoal);
        var allowedEndpoints = status.AllowedEndpointCodes;
        if (endpointCode is null)
        {
            rejected.Add("BlockedByPolicy: controlled production goal could not be mapped to an allowed endpoint.");
        }
        else if (!allowedEndpoints.Contains(endpointCode, StringComparer.OrdinalIgnoreCase))
        {
            rejected.Add($"BlockedByPolicy: endpoint '{endpointCode}' is not in the active production controlled allowlist.");
        }

        var normalizedArtifactTypes = NormalizeArtifactTypes(artifactTypes, options, rejected);
        var normalizedTimeRange = NormalizeTimeRange(timeRange, options, rejected, warnings);
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
            ResolveAnalysisType(normalizedGoal, endpointCode),
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
        if (!EndpointSpecs.TryGetValue(endpointCode, out var endpoint) || endpoint.IsBlockedByPolicy)
        {
            return Result.Invalid("CloudReadonlyProductionControlledPilot endpoint is blocked by policy.");
        }

        var options = controlledOptions.Value;
        var effectiveMaxRows = Math.Clamp(maxRows <= 0 ? intent.MaxRows : maxRows, 1, options.MaxRows);
        var effectiveTimeoutMs = Math.Clamp(timeoutMs <= 0 ? options.TimeoutMs : timeoutMs, 500, options.TimeoutMs);
        var query = BuildQuery(intent, p12Status.PilotWindowId ?? "none", effectiveMaxRows);
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

            var (rows, sourceTruncated) = ExtractRows(document.RootElement, effectiveMaxRows, endpoint.Code, p12Status.PilotWindowId ?? "none", intent.IntentId);
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
                p12Status.PilotWindowId ?? "none",
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
                NormalizeArtifactTypes(artifactTypes, options, artifactRejected));
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
            "sql",
            "payload",
            "写入",
            "创建",
            "更新",
            "删除",
            "任意",
            "完整 payload"
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
        if (endpointCode == "device_logs" &&
            (goal.Contains("exception", StringComparison.OrdinalIgnoreCase) ||
             goal.Contains("alarm", StringComparison.OrdinalIgnoreCase)))
        {
            return "DeviceExceptionAnalysis";
        }

        if (endpointCode == "capacity_summary" &&
            goal.Contains("delivery", StringComparison.OrdinalIgnoreCase))
        {
            return "CapacityDeliveryAnalysis";
        }
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
        CloudReadonlyProductionControlledPilotOptions options,
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
                rejected.Add($"Artifact type '{trimmed}' is not allowed in CloudReadonlyProductionControlledPilot.");
                continue;
            }

            normalized.Add(trimmed);
        }

        return normalized.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static CloudProductionGoalTimeRangeDto NormalizeTimeRange(
        CloudProductionGoalTimeRangeDto? requested,
        CloudReadonlyProductionControlledPilotOptions options,
        ICollection<string> rejected,
        ICollection<string> warnings)
    {
        var now = DateTimeOffset.UtcNow;
        var from = requested?.From ?? now.AddDays(-Math.Min(options.MaxTimeRangeDays, 1));
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
            warnings.Add("timeRange was not provided; defaulted to the last day.");
        }

        return new CloudProductionGoalTimeRangeDto(from, to);
    }

    private static IReadOnlyDictionary<string, string?> BuildQuery(
        CloudProductionGoalIntentDto intent,
        string pilotWindowId,
        int maxRows)
    {
        return new Dictionary<string, string?>
        {
            ["intentId"] = intent.IntentId,
            ["goalHash"] = intent.GoalHash,
            ["analysisType"] = intent.AnalysisType,
            ["maxRows"] = maxRows.ToString(),
            ["from"] = intent.TimeRange.From?.ToString("O"),
            ["to"] = intent.TimeRange.To?.ToString("O"),
            ["boundary"] = CloudReadonlyProductionControlledPilotMarkers.Boundary,
            ["pilotWindowId"] = pilotWindowId
        };
    }

    private static (IReadOnlyCollection<IReadOnlyDictionary<string, object?>> Rows, bool IsTruncated) ExtractRows(
        JsonElement root,
        int maxRows,
        string endpointCode,
        string pilotWindowId,
        string intentId)
    {
        var sourceRows = EnumerateRows(root).ToArray();
        var isTruncated = ReadIsTruncated(root) || sourceRows.Length > maxRows;
        var rows = sourceRows
            .Take(maxRows)
            .Select(row =>
            {
                var normalized = new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase)
                {
                    ["sourceType"] = CloudReadonlyProductionControlledPilotMarkers.SourceType,
                    ["sourceMode"] = CloudReadonlyProductionControlledPilotMarkers.SourceMode,
                    ["isProductionData"] = true,
                    ["isSandbox"] = false,
                    ["isSimulation"] = false,
                    ["sourceLabel"] = CloudReadonlyProductionControlledPilotMarkers.SourceLabel,
                    ["boundary"] = CloudReadonlyProductionControlledPilotMarkers.Boundary,
                    ["pilotWindowId"] = pilotWindowId,
                    ["intentId"] = intentId,
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
