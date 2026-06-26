using System.Diagnostics;
using System.Text.Json;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Options;

namespace AICopilot.AiGatewayService.CloudReadiness;

public sealed class CloudReadonlySandboxAgentTrialService(
    IOptions<CloudReadonlyOptions> cloudReadonlyOptions,
    IOptions<CloudReadonlySandboxOptions> cloudReadonlySandboxOptions,
    IOptions<CloudAiReadOptions> cloudAiReadOptions,
    IOptions<CloudReadonlySandboxAgentTrialOptions> trialOptions,
    ICloudReadonlyReadinessHistoryStore readinessHistoryStore,
    ICloudReadonlySandboxAgentTrialHistoryStore trialHistoryStore,
    ICloudReadonlySandboxClient cloudReadonlySandboxClient)
{
    public static IReadOnlyCollection<string> ScenarioIds => CloudReadonlySandboxAgentTrialScenarioCatalog.ScenarioIds;

    public static bool IsScenarioId(string? scenarioId) =>
        CloudReadonlySandboxAgentTrialScenarioCatalog.IsScenarioId(scenarioId);

    public static string? ResolveScenarioTitle(string? scenarioId) =>
        CloudReadonlySandboxAgentTrialScenarioCatalog.ResolveScenarioTitle(scenarioId);

    public static string? ResolveScenarioDomain(string? scenarioId) =>
        CloudReadonlySandboxAgentTrialScenarioCatalog.ResolveScenarioDomain(scenarioId);

    public static IReadOnlyCollection<string> ResolveScenarioArtifactTypes(string? scenarioId) =>
        CloudReadonlySandboxAgentTrialScenarioCatalog.ResolveScenarioArtifactTypes(scenarioId);

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

        var allowedScenarioIds = CloudReadonlySandboxAgentTrialScenarioCatalog.FilterAllowedScenarioIds(options.AllowedScenarioIds);
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
        if (!CloudReadonlySandboxAgentTrialScenarioCatalog.TryGetScenario(normalizedScenarioId, out var scenario) ||
            !CloudReadonlySandboxAgentTrialScenarioCatalog.FilterAllowedScenarioIds(options.AllowedScenarioIds).Contains(normalizedScenarioId, StringComparer.OrdinalIgnoreCase))
        {
            return Result.Invalid("CloudReadonlySandboxAgentTrial only allows fixed sandbox trial scenarios.");
        }

        if (!CloudReadonlySandboxAgentTrialScenarioCatalog.TryGetEndpoint(scenario.EndpointCode, out var endpoint) || endpoint.IsBlockedByPolicy)
        {
            return Result.Invalid("CloudReadonlySandboxAgentTrial endpoint is blocked by policy.");
        }

        var effectiveMaxRows = Math.Clamp(maxRows <= 0 ? options.MaxRows : maxRows, 1, options.MaxRows);
        var effectiveTimeoutMs = Math.Clamp(timeoutMs <= 0 ? options.TimeoutMs : timeoutMs, 500, options.TimeoutMs);
        var query = CloudReadonlySandboxAgentTrialQueryProjection.BuildQuery(scenario, effectiveMaxRows);
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

            var (rows, sourceTruncated) = CloudReadonlySandboxAgentTrialQueryProjection.ExtractRows(
                document.RootElement,
                effectiveMaxRows,
                endpoint.Code);
            var resultHash = CloudReadonlySandboxAgentTrialQueryProjection.ComputeHash(JsonSerializer.Serialize(rows));
            var queryHash = CloudReadonlySandboxAgentTrialQueryProjection.ComputeHash(
                $"{normalizedScenarioId}|{endpoint.Code}|{effectiveMaxRows}|{CloudReadonlySandboxAgentTrialMarkers.Boundary}");
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
                CloudReadonlySandboxAgentTrialQueryProjection.NormalizeArtifactTypes(artifactTypes, scenario.ArtifactTypes)));
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
}
