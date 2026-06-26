using System.Net;
using System.Security.Cryptography;
using System.Text;
using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.CloudReadiness;

internal static class CloudReadonlyPilotReadinessContractRehearsal
{
    private static readonly IReadOnlyDictionary<string, CloudReadonlyPilotReadinessEndpointSpec> EndpointSpecs =
        new[]
        {
            new CloudReadonlyPilotReadinessEndpointSpec("devices", HttpMethod.Get, "/api/v1/ai/read/devices", 3),
            new CloudReadonlyPilotReadinessEndpointSpec("capacity_summary", HttpMethod.Get, "/api/v1/ai/read/capacity/summary", 2),
            new CloudReadonlyPilotReadinessEndpointSpec("device_logs", HttpMethod.Get, "/api/v1/ai/read/device-logs", 4),
            new CloudReadonlyPilotReadinessEndpointSpec("pass_station_records", HttpMethod.Get, "/api/v1/ai/read/pass-stations/injection", 2),
            new CloudReadonlyPilotReadinessEndpointSpec("write_path", HttpMethod.Post, "/api/v1/ai/read/devices/update", 0, IsBlockedByPolicy: true),
            new CloudReadonlyPilotReadinessEndpointSpec("unknown_endpoint", HttpMethod.Get, "/api/v1/ai/read/unknown", 0, IsBlockedByPolicy: true),
            new CloudReadonlyPilotReadinessEndpointSpec("timeout", HttpMethod.Get, "/api/v1/ai/read/devices", 0),
            new CloudReadonlyPilotReadinessEndpointSpec("http_500", HttpMethod.Get, "/api/v1/ai/read/devices", 0),
            new CloudReadonlyPilotReadinessEndpointSpec("invalid_json", HttpMethod.Get, "/api/v1/ai/read/devices", 0)
        }.ToDictionary(item => item.Code, StringComparer.OrdinalIgnoreCase);

    public static bool IsAllowedEndpointCode(string code)
    {
        return EndpointSpecs.TryGetValue(code, out var spec) &&
               !spec.IsBlockedByPolicy &&
               CloudAiReadEndpointPolicy.IsSafeRouteSegment(code);
    }

    public static CloudReadonlyPilotContractCheckSummaryDto BuildContractSummary(
        CloudReadonlyPilotContractRehearsalDto? rehearsal)
    {
        if (rehearsal is null)
        {
            return new CloudReadonlyPilotContractCheckSummaryDto(0, 0, 0, 0, null);
        }

        return new CloudReadonlyPilotContractCheckSummaryDto(
            rehearsal.Checks.Count,
            rehearsal.Checks.Count(check => check.Status == "Passed"),
            rehearsal.Checks.Count(check => check.Status == "BlockedByPolicy"),
            rehearsal.Checks.Count(check => check.Status is "Failed" or "Timeout" or "SchemaMismatch"),
            rehearsal.GeneratedAt);
    }

    public static PilotApprovalRehearsalStepDto BuildApprovalStep(
        string code,
        string label,
        string packageId,
        DateTimeOffset now,
        bool isBlocking)
    {
        var auditRef = $"audit:p11:{code}:{ComputeHash($"{packageId}|{code}|{now:O}")[..12]}";
        return new PilotApprovalRehearsalStepDto(code, label, "Passed", isBlocking, auditRef);
    }

    public static CloudAiReadEndpointCheckDto BuildFakeContractCheck(
        string endpointCode,
        CloudReadonlyPilotConfigPackageDto package,
        int maxRows,
        int timeoutMs)
    {
        return BuildFakeContractCheck(ResolveEndpointSpec(endpointCode), package, maxRows, timeoutMs);
    }

    public static string ComputeHash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static CloudReadonlyPilotReadinessEndpointSpec ResolveEndpointSpec(string endpointCode)
    {
        var code = endpointCode.Trim();
        return EndpointSpecs.TryGetValue(code, out var spec)
            ? spec
            : new CloudReadonlyPilotReadinessEndpointSpec(code, HttpMethod.Get, $"/api/v1/ai/read/{code}", 0, IsBlockedByPolicy: true);
    }

    private static CloudAiReadEndpointCheckDto BuildFakeContractCheck(
        CloudReadonlyPilotReadinessEndpointSpec spec,
        CloudReadonlyPilotConfigPackageDto package,
        int maxRows,
        int timeoutMs)
    {
        var decision = CloudAiReadEndpointPolicy.Evaluate(spec.Method, spec.Path);
        var endpointAllowedByPackage = package.AllowedEndpointCodes.Contains(spec.Code, StringComparer.OrdinalIgnoreCase);
        if (spec.IsBlockedByPolicy || !decision.IsAllowed || !endpointAllowedByPackage)
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

        var code = spec.Code.ToLowerInvariant();
        if (code is "timeout")
        {
            return FailedFakeCheck(spec, timeoutMs, null, CloudAiReadProblemCodes.Unavailable, "Timeout");
        }

        if (code is "http_500")
        {
            return FailedFakeCheck(spec, 2, (int)HttpStatusCode.InternalServerError, CloudAiReadProblemCodes.Unavailable, "Failed");
        }

        if (code is "invalid_json")
        {
            return FailedFakeCheck(spec, 2, (int)HttpStatusCode.OK, CloudAiReadProblemCodes.Unavailable, "SchemaMismatch");
        }

        var rows = Math.Min(maxRows, spec.FakeRows);
        var isTruncated = spec.FakeRows > maxRows;
        var hash = ComputeHash($"{package.PackageId}|{spec.Code}|{rows}|{isTruncated}|{CloudReadonlyPilotReadinessMarkers.Boundary}");
        return new CloudAiReadEndpointCheckDto(
            spec.Code,
            spec.Method.Method,
            spec.Path,
            "Allowed",
            (int)HttpStatusCode.OK,
            Math.Max(1, spec.Code.Length),
            rows,
            isTruncated,
            hash,
            null,
            "Passed");
    }

    private static CloudAiReadEndpointCheckDto FailedFakeCheck(
        CloudReadonlyPilotReadinessEndpointSpec spec,
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

internal sealed record CloudReadonlyPilotReadinessEndpointSpec(
    string Code,
    HttpMethod Method,
    string Path,
    int FakeRows,
    bool IsBlockedByPolicy = false);
