using System.Net.Http;
using System.Text.Json;
using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.CloudReadiness;

internal static class CloudReadonlyProductionPilotScenarioCatalog
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

    private static readonly string[] pilotArtifactTypes = ["Chart", "Markdown", "Html", "Pdf", "Pptx", "Xlsx"];

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

    public static bool TryGetScenario(string? scenarioId, out ProductionPilotScenario scenario) =>
        Scenarios.TryGetValue(scenarioId?.Trim() ?? string.Empty, out scenario!);

    public static bool TryGetEndpoint(string endpointCode, out EndpointSpec endpoint) =>
        EndpointSpecs.TryGetValue(endpointCode, out endpoint!);

    public static string[] NormalizeEndpointCodes(IEnumerable<string> endpointCodes)
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

    public static IReadOnlyDictionary<string, string?> BuildQuery(
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

    public static IReadOnlyCollection<string> NormalizeArtifactTypes(
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

    public static (IReadOnlyCollection<IReadOnlyDictionary<string, object?>> Rows, bool IsTruncated) ExtractRows(
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
}

internal sealed record ProductionPilotScenario(
    string Id,
    string Title,
    string BusinessDomain,
    string EndpointCode,
    IReadOnlyCollection<string> ArtifactTypes);

internal sealed record EndpointSpec(
    string Code,
    HttpMethod Method,
    string Path,
    bool IsBlockedByPolicy = false);
