using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Options;

namespace AICopilot.AiGatewayService.CloudReadiness;

public static class CloudReadonlyReadinessModes
{
    public const string DryRun = "DryRun";
    public const string FakeEndpoint = "FakeEndpoint";
    public const string RealSandboxSmoke = "RealSandboxSmoke";
}

public static class CloudReadonlyReadinessStatuses
{
    public const string NotConfigured = "NotConfigured";
    public const string ReadyForFake = "ReadyForFake";
    public const string FakePassed = "FakePassed";
    public const string RealSandboxPending = "RealSandboxPending";
    public const string RealSandboxPassed = "RealSandboxPassed";
    public const string Blocked = "Blocked";
    public const string Failed = "Failed";
}

public sealed record CloudAiReadEndpointCheckDto(
    string EndpointCode,
    string Method,
    string Path,
    string PolicyStatus,
    int? HttpStatus,
    long DurationMs,
    int RowCount,
    bool IsTruncated,
    string? ResultHash,
    string? ErrorCode,
    string Status);

public sealed record CloudReadonlySandboxStatusDto(
    string Status,
    bool SandboxEnabled,
    bool BaseUrlConfigured,
    bool TokenConfigured,
    DateTimeOffset? LastSmokeAt,
    IReadOnlyCollection<CloudAiReadEndpointCheckDto> Checks,
    IReadOnlyCollection<string> Errors,
    IReadOnlyCollection<string> Warnings,
    string Boundary = "SandboxSmokeOnly");

public sealed record CloudReadonlyReadinessDto(
    string Status,
    string Mode,
    bool CloudAiReadEnabled,
    bool RealEnabled,
    bool AllowProductionRead,
    bool BaseUrlConfigured,
    bool TokenConfigured,
    DateTimeOffset? LastCheckedAt,
    IReadOnlyCollection<CloudAiReadEndpointCheckDto> Checks,
    IReadOnlyCollection<string> Errors,
    IReadOnlyCollection<string> Warnings,
    string Boundary = "ReadinessOnly",
    CloudReadonlySandboxStatusDto? SandboxStatus = null);

[AuthorizeRequirement("AiGateway.ToolRegistry.Read")]
public sealed record GetCloudReadonlyReadinessQuery : IQuery<Result<CloudReadonlyReadinessDto>>;

[AuthorizeRequirement("AiGateway.ToolRegistry.Read")]
public sealed record GetCloudReadonlyReadinessHistoryQuery : IQuery<Result<IReadOnlyCollection<CloudReadonlyReadinessDto>>>;

[AuthorizeRequirement("AiGateway.ToolRegistry.Read")]
public sealed record GetCloudReadonlySandboxStatusQuery : IQuery<Result<CloudReadonlySandboxStatusDto>>;

[AuthorizeRequirement("AiGateway.ToolRegistry.Read")]
public sealed record GetCloudReadonlySandboxSmokeHistoryQuery : IQuery<Result<IReadOnlyCollection<CloudReadonlySandboxStatusDto>>>;

[AuthorizeRequirement("AiGateway.ToolRegistry.Read")]
public sealed record RunCloudReadonlyReadinessCheckCommand(
    string Mode = CloudReadonlyReadinessModes.FakeEndpoint,
    IReadOnlyCollection<string>? EndpointCodes = null,
    int MaxRows = 20,
    int TimeoutMs = 5000) : ICommand<Result<CloudReadonlyReadinessDto>>;

public sealed class GetCloudReadonlyReadinessQueryHandler(
    CloudReadonlyReadinessService readinessService)
    : IQueryHandler<GetCloudReadonlyReadinessQuery, Result<CloudReadonlyReadinessDto>>
{
    public Task<Result<CloudReadonlyReadinessDto>> Handle(
        GetCloudReadonlyReadinessQuery request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success(readinessService.BuildCurrent()));
    }
}

public sealed class GetCloudReadonlyReadinessHistoryQueryHandler(
    ICloudReadonlyReadinessHistoryStore historyStore)
    : IQueryHandler<GetCloudReadonlyReadinessHistoryQuery, Result<IReadOnlyCollection<CloudReadonlyReadinessDto>>>
{
    public Task<Result<IReadOnlyCollection<CloudReadonlyReadinessDto>>> Handle(
        GetCloudReadonlyReadinessHistoryQuery request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success(historyStore.List()));
    }
}

public sealed class GetCloudReadonlySandboxStatusQueryHandler(
    CloudReadonlyReadinessService readinessService,
    ICloudReadonlyReadinessHistoryStore historyStore)
    : IQueryHandler<GetCloudReadonlySandboxStatusQuery, Result<CloudReadonlySandboxStatusDto>>
{
    public Task<Result<CloudReadonlySandboxStatusDto>> Handle(
        GetCloudReadonlySandboxStatusQuery request,
        CancellationToken cancellationToken)
    {
        var latestSmoke = historyStore.List()
            .FirstOrDefault(report => report.Mode == CloudReadonlyReadinessModes.RealSandboxSmoke);
        return Task.FromResult(Result.Success(readinessService.BuildSandboxStatus(latestSmoke)));
    }
}

public sealed class GetCloudReadonlySandboxSmokeHistoryQueryHandler(
    CloudReadonlyReadinessService readinessService,
    ICloudReadonlyReadinessHistoryStore historyStore)
    : IQueryHandler<GetCloudReadonlySandboxSmokeHistoryQuery, Result<IReadOnlyCollection<CloudReadonlySandboxStatusDto>>>
{
    public Task<Result<IReadOnlyCollection<CloudReadonlySandboxStatusDto>>> Handle(
        GetCloudReadonlySandboxSmokeHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var history = historyStore.List()
            .Where(report => report.Mode == CloudReadonlyReadinessModes.RealSandboxSmoke)
            .Select(readinessService.BuildSandboxStatus)
            .ToArray();
        IReadOnlyCollection<CloudReadonlySandboxStatusDto> result = history;
        return Task.FromResult(Result.Success(result));
    }
}

public sealed class RunCloudReadonlyReadinessCheckCommandHandler(
    CloudReadonlyReadinessService readinessService,
    ICloudReadonlyReadinessHistoryStore historyStore)
    : ICommandHandler<RunCloudReadonlyReadinessCheckCommand, Result<CloudReadonlyReadinessDto>>
{
    public async Task<Result<CloudReadonlyReadinessDto>> Handle(
        RunCloudReadonlyReadinessCheckCommand request,
        CancellationToken cancellationToken)
    {
        var normalizedMode = CloudReadonlyReadinessService.NormalizeMode(request.Mode);
        if (normalizedMode is null)
        {
            return Result.Invalid("CloudReadonly readiness mode must be DryRun, FakeEndpoint, or RealSandboxSmoke.");
        }

        var result = await readinessService.RunAsync(
            normalizedMode,
            request.EndpointCodes,
            request.MaxRows,
            request.TimeoutMs,
            cancellationToken);
        historyStore.Save(result);

        return Result.Success(result);
    }
}

public interface ICloudReadonlyReadinessHistoryStore
{
    void Save(CloudReadonlyReadinessDto report);

    IReadOnlyCollection<CloudReadonlyReadinessDto> List();
}

internal sealed class InMemoryCloudReadonlyReadinessHistoryStore : ICloudReadonlyReadinessHistoryStore
{
    private readonly object _sync = new();
    private readonly List<CloudReadonlyReadinessDto> _items = [];

    public void Save(CloudReadonlyReadinessDto report)
    {
        lock (_sync)
        {
            _items.Insert(0, report);
            if (_items.Count > 20)
            {
                _items.RemoveRange(20, _items.Count - 20);
            }
        }
    }

    public IReadOnlyCollection<CloudReadonlyReadinessDto> List()
    {
        lock (_sync)
        {
            return _items.ToArray();
        }
    }
}

public sealed class CloudReadonlyReadinessService(
    IOptions<CloudReadonlyOptions> cloudReadonlyOptions,
    IOptions<CloudReadonlySandboxOptions> cloudReadonlySandboxOptions,
    IOptions<CloudAiReadOptions> cloudAiReadOptions,
    ICloudReadonlySandboxClient cloudReadonlySandboxClient)
{
    private static readonly IReadOnlyDictionary<string, EndpointSpec> EndpointSpecs =
        new[]
        {
            new EndpointSpec("devices", HttpMethod.Get, "/api/v1/ai/read/devices", 3),
            new EndpointSpec("capacity_summary", HttpMethod.Get, "/api/v1/ai/read/capacity/summary", 2),
            new EndpointSpec("device_logs", HttpMethod.Get, "/api/v1/ai/read/device-logs", 4),
            new EndpointSpec("pass_station_records", HttpMethod.Get, "/api/v1/ai/read/pass-stations/default", 2),
            new EndpointSpec("recipe", HttpMethod.Get, "/api/v1/ai/read/recipes", 0, IsBlockedByPolicy: true),
            new EndpointSpec("recipe_versions", HttpMethod.Get, "/api/v1/ai/read/recipes/versions", 0, IsBlockedByPolicy: true),
            new EndpointSpec("write_path", HttpMethod.Post, "/api/v1/ai/read/devices/update", 0, IsBlockedByPolicy: true),
            new EndpointSpec("timeout", HttpMethod.Get, "/api/v1/ai/read/devices", 0),
            new EndpointSpec("simulate_timeout", HttpMethod.Get, "/api/v1/ai/read/devices", 0),
            new EndpointSpec("http_401", HttpMethod.Get, "/api/v1/ai/read/devices", 0),
            new EndpointSpec("unauthorized", HttpMethod.Get, "/api/v1/ai/read/devices", 0),
            new EndpointSpec("http_403", HttpMethod.Get, "/api/v1/ai/read/devices", 0),
            new EndpointSpec("forbidden", HttpMethod.Get, "/api/v1/ai/read/devices", 0),
            new EndpointSpec("http_404", HttpMethod.Get, "/api/v1/ai/read/devices", 0),
            new EndpointSpec("not_found", HttpMethod.Get, "/api/v1/ai/read/devices", 0),
            new EndpointSpec("http_500", HttpMethod.Get, "/api/v1/ai/read/devices", 0),
            new EndpointSpec("server_error", HttpMethod.Get, "/api/v1/ai/read/devices", 0),
            new EndpointSpec("invalid_json", HttpMethod.Get, "/api/v1/ai/read/devices", 0),
            new EndpointSpec("schema_mismatch", HttpMethod.Get, "/api/v1/ai/read/devices", 0)
        }.ToDictionary(item => item.Code, StringComparer.OrdinalIgnoreCase);

    private static readonly string[] DefaultEndpointCodes =
    [
        "devices",
        "capacity_summary",
        "device_logs",
        "pass_station_records"
    ];

    public static string? NormalizeMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return CloudReadonlyReadinessModes.FakeEndpoint;
        }

        if (mode.Equals(CloudReadonlyReadinessModes.DryRun, StringComparison.OrdinalIgnoreCase))
        {
            return CloudReadonlyReadinessModes.DryRun;
        }

        if (mode.Equals(CloudReadonlyReadinessModes.FakeEndpoint, StringComparison.OrdinalIgnoreCase))
        {
            return CloudReadonlyReadinessModes.FakeEndpoint;
        }

        if (mode.Equals(CloudReadonlyReadinessModes.RealSandboxSmoke, StringComparison.OrdinalIgnoreCase))
        {
            return CloudReadonlyReadinessModes.RealSandboxSmoke;
        }

        return null;
    }

    public CloudReadonlyReadinessDto BuildCurrent()
    {
        var (errors, warnings) = ValidateConfiguration(CloudReadonlyReadinessModes.DryRun);
        return BuildReport(
            CloudReadonlyReadinessModes.DryRun,
            errors.Count == 0 ? CloudReadonlyReadinessStatuses.ReadyForFake : CloudReadonlyReadinessStatuses.Blocked,
            null,
            [],
            errors,
            warnings);
    }

    public CloudReadonlySandboxStatusDto BuildSandboxStatus(CloudReadonlyReadinessDto? latestSmoke = null)
    {
        var sandbox = cloudReadonlySandboxOptions.Value;
        var (errors, warnings) = ValidateSandboxConfiguration();
        if (latestSmoke?.Mode == CloudReadonlyReadinessModes.RealSandboxSmoke)
        {
            return latestSmoke.SandboxStatus ?? new CloudReadonlySandboxStatusDto(
                latestSmoke.Status,
                sandbox.Enabled,
                !string.IsNullOrWhiteSpace(sandbox.BaseUrl),
                !string.IsNullOrWhiteSpace(sandbox.ServiceAccountToken),
                latestSmoke.LastCheckedAt,
                latestSmoke.Checks,
                latestSmoke.Errors,
                latestSmoke.Warnings);
        }

        var status = errors.Count > 0
            ? CloudReadonlyReadinessStatuses.Blocked
            : sandbox.IsConfigured()
                ? CloudReadonlyReadinessStatuses.RealSandboxPending
                : CloudReadonlyReadinessStatuses.NotConfigured;

        return new CloudReadonlySandboxStatusDto(
            status,
            sandbox.Enabled,
            !string.IsNullOrWhiteSpace(sandbox.BaseUrl),
            !string.IsNullOrWhiteSpace(sandbox.ServiceAccountToken),
            null,
            [],
            errors,
            warnings);
    }

    public async Task<CloudReadonlyReadinessDto> RunAsync(
        string mode,
        IReadOnlyCollection<string>? endpointCodes,
        int maxRows,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var (errors, warnings) = ValidateConfiguration(mode);
        var effectiveMaxRows = Math.Clamp(maxRows, 1, 50);
        var effectiveTimeoutMs = Math.Clamp(timeoutMs, 500, 30_000);

        var specs = ResolveEndpointSpecs(endpointCodes);
        var checks = new List<CloudAiReadEndpointCheckDto>();
        if (mode == CloudReadonlyReadinessModes.DryRun)
        {
            checks.AddRange(specs.Select(spec => BuildDryRunCheck(spec)));
        }
        else if (mode == CloudReadonlyReadinessModes.FakeEndpoint)
        {
            checks.AddRange(specs.Select(spec => BuildFakeEndpointCheck(spec, effectiveMaxRows, effectiveTimeoutMs)));
        }
        else if (errors.Count > 0)
        {
            checks.AddRange(specs.Select(spec => BuildSkippedSandboxCheck(
                spec,
                CloudAiReadProblemCodes.RequestBlocked)));
        }
        else
        {
            checks.AddRange(await RunRealSandboxSmokeAsync(specs, effectiveMaxRows, effectiveTimeoutMs, cancellationToken));
        }

        var status = ResolveStatus(mode, errors, checks);
        return BuildReport(
            mode,
            status,
            DateTimeOffset.UtcNow,
            checks,
            errors,
            warnings);
    }

    private (List<string> Errors, List<string> Warnings) ValidateConfiguration(string mode)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var cloudReadonly = cloudReadonlyOptions.Value;
        var cloudAiRead = cloudAiReadOptions.Value;
        var sandbox = cloudReadonlySandboxOptions.Value;

        if (!Enum.IsDefined(cloudReadonly.Mode))
        {
            errors.Add("CloudReadonly.Mode must be Disabled, Simulation, or Real.");
        }

        if (!string.IsNullOrWhiteSpace(cloudAiRead.BaseUrl) &&
            (!Uri.TryCreate(cloudAiRead.BaseUrl, UriKind.Absolute, out var baseUri) ||
             (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps)))
        {
            errors.Add("CloudAiRead.BaseUrl must be an absolute HTTP/HTTPS URL when configured.");
        }

        if (cloudAiRead.TimeoutSeconds is < 1 or > 30)
        {
            errors.Add("CloudAiRead.TimeoutSeconds must be between 1 and 30.");
        }

        if (!CloudAiReadEndpointPolicy.IsSafeRouteSegment(cloudAiRead.DefaultPassStationTypeKey))
        {
            errors.Add("CloudAiRead.DefaultPassStationTypeKey must be a single safe route segment.");
        }

        foreach (var path in cloudAiRead.ExplicitPostQueryPaths)
        {
            var decision = CloudAiReadEndpointPolicy.Evaluate(HttpMethod.Post, path, cloudAiRead.ExplicitPostQueryPaths);
            if (!decision.IsAllowed)
            {
                errors.Add($"CloudAiRead explicit POST path '{path}' is not readiness-safe: {decision.Reason}");
            }
        }

        var (sandboxErrors, sandboxWarnings) = ValidateSandboxConfiguration();
        errors.AddRange(sandboxErrors);
        if (mode == CloudReadonlyReadinessModes.RealSandboxSmoke)
        {
            warnings.AddRange(sandboxWarnings);
        }

        if (mode != CloudReadonlyReadinessModes.RealSandboxSmoke)
        {
            if (cloudReadonly.Mode != CloudReadonlyDataSourceMode.Disabled)
            {
                errors.Add("CloudReadonly.Mode must remain Disabled during P5/P6 DryRun/FakeEndpoint readiness.");
            }

            if (cloudReadonly.Real.Enabled)
            {
                errors.Add("CloudReadonly.Real.Enabled must remain false by default during P5/P6 readiness.");
            }

            if (cloudReadonly.Real.AllowProductionRead)
            {
                errors.Add("CloudReadonly.Real.AllowProductionRead must remain false by default during P5/P6 readiness.");
            }

            if (cloudAiRead.Enabled)
            {
                errors.Add("CloudAiRead.Enabled must remain false by default during P5/P6 readiness.");
            }
        }
        else
        {
            if (cloudReadonly.Mode != CloudReadonlyDataSourceMode.Disabled)
            {
                errors.Add("CloudReadonly.Mode must remain Disabled during P6 sandbox smoke.");
            }

            if (cloudReadonly.Real.Enabled)
            {
                errors.Add("CloudReadonly.Real.Enabled must remain false during P6 sandbox smoke.");
            }

            if (cloudReadonly.Real.AllowProductionRead)
            {
                errors.Add("CloudReadonly.Real.AllowProductionRead must remain false during P6 sandbox smoke.");
            }

            if (cloudAiRead.Enabled)
            {
                errors.Add("CloudAiRead.Enabled must remain false during P6 sandbox smoke; use CloudReadonlySandbox only.");
            }

            if (!sandbox.IsConfigured())
            {
                warnings.Add("RealSandboxSmoke is pending because CloudReadonlySandbox is disabled or incomplete.");
            }
        }

        var cloudReadonlyTool = BuiltInToolRegistrations.AgentRuntimeTools
            .FirstOrDefault(tool => tool.ToolCode == "query_cloud_data_readonly");
        if (cloudReadonlyTool is null)
        {
            errors.Add("Tool Registry is missing query_cloud_data_readonly.");
        }
        else
        {
            if (cloudReadonlyTool.IsEnabled)
            {
                errors.Add("query_cloud_data_readonly must remain disabled by default in P5/P6.");
            }

            if (cloudReadonlyTool.IsVisibleToPlanner)
            {
                errors.Add("query_cloud_data_readonly must not be visible to Planner in P5/P6.");
            }

            if (cloudReadonlyTool.IsExecutableByAgent)
            {
                errors.Add("query_cloud_data_readonly must not be executable by Agent in P5/P6.");
            }
        }

        if (string.IsNullOrWhiteSpace(cloudAiRead.BaseUrl))
        {
            warnings.Add("CloudAiRead.BaseUrl is not configured; fake readiness does not require it.");
        }

        if (string.IsNullOrWhiteSpace(cloudAiRead.ServiceAccountToken))
        {
            warnings.Add("CloudAiRead token is not configured; token value is never returned by readiness APIs.");
        }

        return (errors, warnings);
    }

    private (List<string> Errors, List<string> Warnings) ValidateSandboxConfiguration()
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var sandbox = cloudReadonlySandboxOptions.Value;

        if (!string.IsNullOrWhiteSpace(sandbox.BaseUrl) &&
            (!Uri.TryCreate(sandbox.BaseUrl, UriKind.Absolute, out var baseUri) ||
             (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps)))
        {
            errors.Add("CloudReadonlySandbox.BaseUrl must be an absolute HTTP/HTTPS URL when configured.");
        }

        if (sandbox.TimeoutSeconds is < 1 or > 30)
        {
            errors.Add("CloudReadonlySandbox.TimeoutSeconds must be between 1 and 30.");
        }

        if (!CloudAiReadEndpointPolicy.IsSafeRouteSegment(sandbox.DefaultPassStationTypeKey))
        {
            errors.Add("CloudReadonlySandbox.DefaultPassStationTypeKey must be a single safe route segment.");
        }

        foreach (var path in sandbox.ExplicitPostQueryPaths)
        {
            var decision = CloudAiReadEndpointPolicy.Evaluate(HttpMethod.Post, path, sandbox.ExplicitPostQueryPaths);
            if (!decision.IsAllowed)
            {
                errors.Add($"CloudReadonlySandbox explicit POST path '{path}' is not sandbox-safe: {decision.Reason}");
            }
        }

        if (!sandbox.Enabled)
        {
            warnings.Add("CloudReadonlySandbox.Enabled is false; sandbox smoke stays disabled.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(sandbox.BaseUrl))
            {
                warnings.Add("CloudReadonlySandbox.BaseUrl is not configured.");
            }

            if (string.IsNullOrWhiteSpace(sandbox.ServiceAccountToken))
            {
                warnings.Add("CloudReadonlySandbox token is not configured; token value is never returned by readiness APIs.");
            }
        }

        return (errors, warnings);
    }

    private static IReadOnlyCollection<EndpointSpec> ResolveEndpointSpecs(IReadOnlyCollection<string>? endpointCodes)
    {
        var codes = endpointCodes is null || endpointCodes.Count == 0
            ? DefaultEndpointCodes
            : endpointCodes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        return codes.Select(code =>
            EndpointSpecs.TryGetValue(code, out var spec)
                ? spec
                : new EndpointSpec(code, HttpMethod.Get, $"/api/v1/ai/read/{code}", 0, IsBlockedByPolicy: true))
            .ToArray();
    }

    private static CloudAiReadEndpointCheckDto BuildDryRunCheck(EndpointSpec spec)
    {
        var decision = CloudAiReadEndpointPolicy.Evaluate(spec.Method, spec.Path);
        var blocked = spec.IsBlockedByPolicy || !decision.IsAllowed;
        return new CloudAiReadEndpointCheckDto(
            spec.Code,
            spec.Method.Method,
            spec.Path,
            blocked ? "Blocked" : "Allowed",
            null,
            0,
            0,
            false,
            null,
            blocked ? CloudAiReadProblemCodes.RequestBlocked : null,
            blocked ? "BlockedByPolicy" : "Ready");
    }

    private static CloudAiReadEndpointCheckDto BuildSkippedSandboxCheck(
        EndpointSpec spec,
        string errorCode)
    {
        return new CloudAiReadEndpointCheckDto(
            spec.Code,
            spec.Method.Method,
            spec.Path,
            "Skipped",
            null,
            0,
            0,
            false,
            null,
            errorCode,
            "Skipped");
    }

    private static CloudAiReadEndpointCheckDto BuildFakeEndpointCheck(
        EndpointSpec spec,
        int maxRows,
        int timeoutMs)
    {
        var decision = CloudAiReadEndpointPolicy.Evaluate(spec.Method, spec.Path);
        if (spec.IsBlockedByPolicy || !decision.IsAllowed)
        {
            return new CloudAiReadEndpointCheckDto(
                spec.Code,
                spec.Method.Method,
                spec.Path,
                "Blocked",
                (int)HttpStatusCode.Forbidden,
                1,
                0,
                false,
                null,
                CloudAiReadProblemCodes.RequestBlocked,
                "BlockedByPolicy");
        }

        if (TryBuildSimulatedFailure(spec, timeoutMs, out var failure))
        {
            return failure;
        }

        var rows = Math.Min(maxRows, spec.FakeRows);
        var isTruncated = spec.FakeRows > maxRows;
        var payloadHash = ComputeHash($"{spec.Code}|{spec.Method.Method}|{spec.Path}|{rows}|{isTruncated}|ReadinessOnly");
        return new CloudAiReadEndpointCheckDto(
            spec.Code,
            spec.Method.Method,
            spec.Path,
            "Allowed",
            (int)HttpStatusCode.OK,
            Math.Max(1, spec.Code.Length),
            rows,
            isTruncated,
            payloadHash,
            null,
            "Passed");
    }

    private async Task<IReadOnlyCollection<CloudAiReadEndpointCheckDto>> RunRealSandboxSmokeAsync(
        IReadOnlyCollection<EndpointSpec> specs,
        int maxRows,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var sandbox = cloudReadonlySandboxOptions.Value;
        if (!sandbox.IsConfigured())
        {
            return specs.Select(spec => new CloudAiReadEndpointCheckDto(
                    spec.Code,
                    spec.Method.Method,
                    spec.Path,
                    "Skipped",
                    null,
                    0,
                    0,
                    false,
                    null,
                    CloudAiReadProblemCodes.NotConfigured,
                    "Skipped"))
                .ToArray();
        }

        var checks = new List<CloudAiReadEndpointCheckDto>();
        foreach (var spec in specs)
        {
            var decision = CloudAiReadEndpointPolicy.Evaluate(
                spec.Method,
                spec.Path,
                sandbox.ExplicitPostQueryPaths);
            if (spec.IsBlockedByPolicy || !decision.IsAllowed)
            {
                checks.Add(BuildDryRunCheck(spec) with { HttpStatus = (int)HttpStatusCode.Forbidden });
                continue;
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));
                using var document = await cloudReadonlySandboxClient.SendJsonAsync(
                    sandbox,
                    spec.Method,
                    spec.Path,
                    BuildSmokeQuery(spec, maxRows),
                    timeoutCts.Token);
                stopwatch.Stop();
                var rowCount = CountRows(document.RootElement, maxRows);
                checks.Add(new CloudAiReadEndpointCheckDto(
                    spec.Code,
                    spec.Method.Method,
                    spec.Path,
                    "Allowed",
                    (int)HttpStatusCode.OK,
                    stopwatch.ElapsedMilliseconds,
                    rowCount,
                    rowCount >= maxRows,
                    ComputeHash($"{spec.Code}|{rowCount}|{document.RootElement.ValueKind}|SandboxSmokeOnly"),
                    null,
                    "Passed"));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                stopwatch.Stop();
                checks.Add(FailedRealCheck(spec, stopwatch.ElapsedMilliseconds, null, CloudAiReadProblemCodes.Unavailable, "Timeout"));
            }
            catch (CloudAiReadException ex)
            {
                stopwatch.Stop();
                checks.Add(FailedRealCheck(spec, stopwatch.ElapsedMilliseconds, (int?)ex.StatusCode, ex.Code, "Failed"));
            }
            catch (JsonException)
            {
                stopwatch.Stop();
                checks.Add(FailedRealCheck(spec, stopwatch.ElapsedMilliseconds, (int)HttpStatusCode.OK, CloudAiReadProblemCodes.Unavailable, "SchemaMismatch"));
            }
        }

        return checks;
    }

    private static bool TryBuildSimulatedFailure(
        EndpointSpec spec,
        int timeoutMs,
        out CloudAiReadEndpointCheckDto failure)
    {
        var code = spec.Code.ToLowerInvariant();
        if (code is "timeout" or "simulate_timeout")
        {
            failure = FailedFakeCheck(spec, timeoutMs, null, CloudAiReadProblemCodes.Unavailable, "Timeout");
            return true;
        }

        if (code is "http_401" or "unauthorized")
        {
            failure = FailedFakeCheck(spec, 2, (int)HttpStatusCode.Unauthorized, CloudAiReadProblemCodes.Unauthorized, "Failed");
            return true;
        }

        if (code is "http_403" or "forbidden")
        {
            failure = FailedFakeCheck(spec, 2, (int)HttpStatusCode.Forbidden, CloudAiReadProblemCodes.Forbidden, "Failed");
            return true;
        }

        if (code is "http_404" or "not_found")
        {
            failure = FailedFakeCheck(spec, 2, (int)HttpStatusCode.NotFound, CloudAiReadProblemCodes.NotFound, "Failed");
            return true;
        }

        if (code is "http_500" or "server_error")
        {
            failure = FailedFakeCheck(spec, 2, (int)HttpStatusCode.InternalServerError, CloudAiReadProblemCodes.Unavailable, "Failed");
            return true;
        }

        if (code is "invalid_json" or "schema_mismatch")
        {
            failure = FailedFakeCheck(spec, 2, (int)HttpStatusCode.OK, CloudAiReadProblemCodes.Unavailable, "SchemaMismatch");
            return true;
        }

        failure = default!;
        return false;
    }

    private static CloudAiReadEndpointCheckDto FailedFakeCheck(
        EndpointSpec spec,
        long durationMs,
        int? httpStatus,
        string errorCode,
        string status)
    {
        return new CloudAiReadEndpointCheckDto(
            spec.Code,
            spec.Method.Method,
            spec.Path,
            "Allowed",
            httpStatus,
            durationMs,
            0,
            false,
            null,
            errorCode,
            status);
    }

    private static CloudAiReadEndpointCheckDto FailedRealCheck(
        EndpointSpec spec,
        long durationMs,
        int? httpStatus,
        string errorCode,
        string status)
    {
        return new CloudAiReadEndpointCheckDto(
            spec.Code,
            spec.Method.Method,
            spec.Path,
            "Allowed",
            httpStatus,
            durationMs,
            0,
            false,
            null,
            errorCode,
            status);
    }

    private static IReadOnlyDictionary<string, string?> BuildSmokeQuery(EndpointSpec spec, int maxRows)
    {
        var query = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["maxRows"] = maxRows.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        if (spec.Code is "capacity_summary" or "device_logs" or "pass_station_records")
        {
            query["deviceId"] = "READINESS-DEVICE";
            query["deviceCode"] = "READINESS-DEVICE";
            query["startDate"] = "2026-01-01";
            query["endDate"] = "2026-01-02";
            query["startTime"] = "2026-01-01T00:00:00Z";
            query["endTime"] = "2026-01-02T00:00:00Z";
        }

        return query;
    }

    private CloudReadonlyReadinessDto BuildReport(
        string mode,
        string status,
        DateTimeOffset? checkedAt,
        IReadOnlyCollection<CloudAiReadEndpointCheckDto> checks,
        IReadOnlyCollection<string> errors,
        IReadOnlyCollection<string> warnings)
    {
        var cloudReadonly = cloudReadonlyOptions.Value;
        var cloudAiRead = cloudAiReadOptions.Value;
        var boundary = mode == CloudReadonlyReadinessModes.RealSandboxSmoke
            ? "SandboxSmokeOnly"
            : "ReadinessOnly";
        var sandboxStatus = mode == CloudReadonlyReadinessModes.RealSandboxSmoke
            ? new CloudReadonlySandboxStatusDto(
                status,
                cloudReadonlySandboxOptions.Value.Enabled,
                !string.IsNullOrWhiteSpace(cloudReadonlySandboxOptions.Value.BaseUrl),
                !string.IsNullOrWhiteSpace(cloudReadonlySandboxOptions.Value.ServiceAccountToken),
                checkedAt,
                checks,
                errors,
                warnings)
            : BuildSandboxStatus();

        return new CloudReadonlyReadinessDto(
            status,
            mode,
            cloudAiRead.Enabled,
            cloudReadonly.Real.Enabled,
            cloudReadonly.Real.AllowProductionRead,
            !string.IsNullOrWhiteSpace(cloudAiRead.BaseUrl),
            !string.IsNullOrWhiteSpace(cloudAiRead.ServiceAccountToken),
            checkedAt,
            checks,
            errors,
            warnings,
            boundary,
            sandboxStatus);
    }

    private static string ResolveStatus(
        string mode,
        IReadOnlyCollection<string> errors,
        IReadOnlyCollection<CloudAiReadEndpointCheckDto> checks)
    {
        if (errors.Count > 0)
        {
            return CloudReadonlyReadinessStatuses.Blocked;
        }

        if (checks.Any(check => check.Status == "BlockedByPolicy"))
        {
            return CloudReadonlyReadinessStatuses.Blocked;
        }

        if (checks.Any(check => check.Status is "Failed" or "Timeout" or "SchemaMismatch"))
        {
            return CloudReadonlyReadinessStatuses.Failed;
        }

        if (mode == CloudReadonlyReadinessModes.RealSandboxSmoke)
        {
            return checks.Any(check => check.Status == "Skipped")
                ? CloudReadonlyReadinessStatuses.RealSandboxPending
                : CloudReadonlyReadinessStatuses.RealSandboxPassed;
        }

        if (mode == CloudReadonlyReadinessModes.FakeEndpoint)
        {
            return CloudReadonlyReadinessStatuses.FakePassed;
        }

        return CloudReadonlyReadinessStatuses.ReadyForFake;
    }

    private static int CountRows(JsonElement root, int maxRows)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return Math.Min(maxRows, root.GetArrayLength());
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "items", "data", "records", "results" })
            {
                if (root.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array)
                {
                    return Math.Min(maxRows, array.GetArrayLength());
                }
            }

            return 1;
        }

        return 0;
    }

    private static string ComputeHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private sealed record EndpointSpec(
        string Code,
        HttpMethod Method,
        string Path,
        int FakeRows,
        bool IsBlockedByPolicy = false);
}
