using System.Net;
using System.Security.Cryptography;
using System.Text;
using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.CloudReadiness;

internal static class CloudReadonlyReadinessEndpointCatalog
{
    private static readonly IReadOnlyDictionary<string, CloudReadonlyReadinessEndpointSpec> EndpointSpecs =
        new[]
        {
            new CloudReadonlyReadinessEndpointSpec("devices", HttpMethod.Get, "/api/v1/ai/read/devices", 3),
            new CloudReadonlyReadinessEndpointSpec("capacity_summary", HttpMethod.Get, "/api/v1/ai/read/capacity/summary", 2),
            new CloudReadonlyReadinessEndpointSpec("device_logs", HttpMethod.Get, "/api/v1/ai/read/device-logs", 4),
            new CloudReadonlyReadinessEndpointSpec("pass_station_records", HttpMethod.Get, "/api/v1/ai/read/pass-stations/injection", 2),
            new CloudReadonlyReadinessEndpointSpec("write_path", HttpMethod.Post, "/api/v1/ai/read/devices/update", 0, IsBlockedByPolicy: true),
            new CloudReadonlyReadinessEndpointSpec("timeout", HttpMethod.Get, "/api/v1/ai/read/devices", 0),
            new CloudReadonlyReadinessEndpointSpec("simulate_timeout", HttpMethod.Get, "/api/v1/ai/read/devices", 0),
            new CloudReadonlyReadinessEndpointSpec("http_401", HttpMethod.Get, "/api/v1/ai/read/devices", 0),
            new CloudReadonlyReadinessEndpointSpec("unauthorized", HttpMethod.Get, "/api/v1/ai/read/devices", 0),
            new CloudReadonlyReadinessEndpointSpec("http_403", HttpMethod.Get, "/api/v1/ai/read/devices", 0),
            new CloudReadonlyReadinessEndpointSpec("forbidden", HttpMethod.Get, "/api/v1/ai/read/devices", 0),
            new CloudReadonlyReadinessEndpointSpec("http_404", HttpMethod.Get, "/api/v1/ai/read/devices", 0),
            new CloudReadonlyReadinessEndpointSpec("not_found", HttpMethod.Get, "/api/v1/ai/read/devices", 0),
            new CloudReadonlyReadinessEndpointSpec("http_500", HttpMethod.Get, "/api/v1/ai/read/devices", 0),
            new CloudReadonlyReadinessEndpointSpec("server_error", HttpMethod.Get, "/api/v1/ai/read/devices", 0),
            new CloudReadonlyReadinessEndpointSpec("invalid_json", HttpMethod.Get, "/api/v1/ai/read/devices", 0),
            new CloudReadonlyReadinessEndpointSpec("schema_mismatch", HttpMethod.Get, "/api/v1/ai/read/devices", 0)
        }.ToDictionary(item => item.Code, StringComparer.OrdinalIgnoreCase);

    private static readonly string[] DefaultEndpointCodes =
    [
        "devices",
        "capacity_summary",
        "device_logs",
        "pass_station_records"
    ];

    public static IReadOnlyCollection<CloudReadonlyReadinessEndpointSpec> ResolveEndpointSpecs(
        IReadOnlyCollection<string>? endpointCodes)
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
                : new CloudReadonlyReadinessEndpointSpec(code, HttpMethod.Get, $"/api/v1/ai/read/{code}", 0, IsBlockedByPolicy: true))
            .ToArray();
    }

    public static CloudAiReadEndpointCheckDto BuildDryRunCheck(CloudReadonlyReadinessEndpointSpec spec)
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

    public static CloudAiReadEndpointCheckDto BuildSkippedSandboxCheck(
        CloudReadonlyReadinessEndpointSpec spec,
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

    public static CloudAiReadEndpointCheckDto BuildFakeEndpointCheck(
        CloudReadonlyReadinessEndpointSpec spec,
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

    public static string ComputeHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool TryBuildSimulatedFailure(
        CloudReadonlyReadinessEndpointSpec spec,
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
        CloudReadonlyReadinessEndpointSpec spec,
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
}

internal sealed record CloudReadonlyReadinessEndpointSpec(
    string Code,
    HttpMethod Method,
    string Path,
    int FakeRows,
    bool IsBlockedByPolicy = false);
