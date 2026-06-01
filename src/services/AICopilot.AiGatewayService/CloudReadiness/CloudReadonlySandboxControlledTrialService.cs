using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Options;

namespace AICopilot.AiGatewayService.CloudReadiness;

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

        var allowedEndpoints = CloudReadonlySandboxControlledTrialGoalPolicy
            .FilterAllowedEndpointCodes(options.AllowedEndpointCodes);
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
        var normalizedGoal = CloudReadonlySandboxControlledTrialGoalPolicy.NormalizeGoal(goal);
        if (string.IsNullOrWhiteSpace(normalizedGoal))
        {
            rejected.Add("Goal is required.");
        }

        if (CloudReadonlySandboxControlledTrialGoalPolicy.ContainsBlockedGoalTerm(normalizedGoal))
        {
            rejected.Add("BlockedByPolicy: controlled sandbox goal cannot request Recipe, write paths, production paths, or Cloud write semantics.");
        }

        var endpointCode = CloudReadonlySandboxControlledTrialGoalPolicy.ResolveEndpointCode(normalizedGoal);
        var allowedEndpoints = CloudReadonlySandboxControlledTrialGoalPolicy
            .FilterAllowedEndpointCodes(options.AllowedEndpointCodes);
        if (endpointCode is null)
        {
            rejected.Add("BlockedByPolicy: controlled sandbox goal could not be mapped to an allowed endpoint.");
        }
        else if (!allowedEndpoints.Contains(endpointCode, StringComparer.OrdinalIgnoreCase))
        {
            rejected.Add($"BlockedByPolicy: endpoint '{endpointCode}' is not in CloudReadonlySandboxControlledTrial allowlist.");
        }

        var normalizedArtifactTypes = CloudReadonlySandboxControlledTrialGoalPolicy.NormalizeArtifactTypes(
            artifactTypes,
            options,
            rejected);
        var normalizedTimeRange = CloudReadonlySandboxControlledTrialGoalPolicy.NormalizeTimeRange(
            timeRange,
            options,
            rejected,
            warnings);
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
            CloudReadonlySandboxControlledTrialGoalPolicy.ResolveAnalysisType(normalizedGoal, endpointCode),
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
        var validation = ValidateIntent(intent, requireIntentStoreEntry: false);
        if (!validation.IsSuccess)
        {
            return Result.From(validation);
        }

        var endpointCode = intent.EndpointCodes.First();
        if (!CloudReadonlySandboxControlledTrialGoalPolicy.TryGetEndpoint(endpointCode, out var endpoint) ||
            endpoint.IsBlockedByPolicy)
        {
            return Result.Invalid("CloudReadonlySandboxControlledTrial endpoint is blocked by policy.");
        }

        var options = controlledOptions.Value;
        var effectiveMaxRows = Math.Clamp(maxRows <= 0 ? intent.MaxRows : maxRows, 1, options.MaxRows);
        var effectiveTimeoutMs = Math.Clamp(timeoutMs <= 0 ? options.TimeoutMs : timeoutMs, 500, options.TimeoutMs);
        var query = CloudReadonlySandboxControlledTrialQueryProjection.BuildQuery(intent, effectiveMaxRows);
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

            var (rows, sourceTruncated) = CloudReadonlySandboxControlledTrialQueryProjection.ExtractRows(
                document.RootElement,
                effectiveMaxRows,
                endpoint.Code);
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
                CloudReadonlySandboxControlledTrialGoalPolicy.NormalizeArtifactTypes(
                    artifactTypes,
                    options,
                    artifactRejected),
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
        var allowedEndpoints = CloudReadonlySandboxControlledTrialGoalPolicy
            .FilterAllowedEndpointCodes(options.AllowedEndpointCodes);
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

    private static string ComputeHash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
