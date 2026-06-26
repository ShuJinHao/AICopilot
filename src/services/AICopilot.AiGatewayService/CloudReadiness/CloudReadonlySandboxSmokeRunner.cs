using System.Diagnostics;
using System.Net;
using System.Text.Json;
using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.CloudReadiness;

internal static class CloudReadonlySandboxSmokeRunner
{
    public static async Task<IReadOnlyCollection<CloudAiReadEndpointCheckDto>> RunAsync(
        ICloudReadonlySandboxClient cloudReadonlySandboxClient,
        CloudReadonlySandboxOptions sandbox,
        IReadOnlyCollection<CloudReadonlyReadinessEndpointSpec> specs,
        int maxRows,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
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
                checks.Add(CloudReadonlyReadinessEndpointCatalog.BuildDryRunCheck(spec) with
                {
                    HttpStatus = (int)HttpStatusCode.Forbidden
                });
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
                    CloudReadonlyReadinessEndpointCatalog.ComputeHash($"{spec.Code}|{rowCount}|{document.RootElement.ValueKind}|SandboxSmokeOnly"),
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

    private static IReadOnlyDictionary<string, string?> BuildSmokeQuery(
        CloudReadonlyReadinessEndpointSpec spec,
        int maxRows)
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

    private static CloudAiReadEndpointCheckDto FailedRealCheck(
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
