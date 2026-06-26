using System.Net.Http;

namespace AICopilot.AiGatewayService.CloudReadiness;

internal static class CloudReadonlySandboxAgentTrialScenarioCatalog
{
    private static readonly IReadOnlyDictionary<string, SandboxTrialScenario> Scenarios =
        new[]
        {
            new SandboxTrialScenario(
                "cloud-sandbox-devices",
                "设备清单",
                "Device",
                "devices",
                ["Markdown", "Html"]),
            new SandboxTrialScenario(
                "cloud-sandbox-capacity-summary",
                "产能汇总",
                "Capacity",
                "capacity_summary",
                ["Chart", "Markdown", "Html", "Pptx"]),
            new SandboxTrialScenario(
                "cloud-sandbox-device-logs",
                "设备日志",
                "Equipment",
                "device_logs",
                ["Markdown", "Html", "Xlsx"]),
            new SandboxTrialScenario(
                "cloud-sandbox-pass-station-records",
                "过站记录",
                "Production",
                "pass_station_records",
                ["Chart", "Markdown", "Html", "Xlsx"]),
            new SandboxTrialScenario(
                "cloud-sandbox-device-exception-analysis",
                "设备异常分析",
                "Equipment",
                "device_logs",
                ["Chart", "Markdown", "Html", "Pdf"]),
            new SandboxTrialScenario(
                "cloud-sandbox-capacity-delivery-analysis",
                "产能交付分析",
                "Delivery",
                "capacity_summary",
                ["Chart", "Markdown", "Html", "Pptx", "Xlsx"])
        }.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, EndpointSpec> EndpointSpecs =
        new[]
        {
            new EndpointSpec("devices", HttpMethod.Get, "/api/v1/ai/read/devices"),
            new EndpointSpec("capacity_summary", HttpMethod.Get, "/api/v1/ai/read/capacity/summary"),
            new EndpointSpec("device_logs", HttpMethod.Get, "/api/v1/ai/read/device-logs"),
            new EndpointSpec("pass_station_records", HttpMethod.Get, "/api/v1/ai/read/pass-stations/injection"),
            new EndpointSpec("write_path", HttpMethod.Post, "/api/v1/ai/read/devices/update", IsBlockedByPolicy: true)
        }.ToDictionary(item => item.Code, StringComparer.OrdinalIgnoreCase);

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

    public static bool TryGetScenario(string? scenarioId, out SandboxTrialScenario scenario) =>
        Scenarios.TryGetValue(scenarioId?.Trim() ?? string.Empty, out scenario!);

    public static bool TryGetEndpoint(string endpointCode, out EndpointSpec endpoint) =>
        EndpointSpecs.TryGetValue(endpointCode, out endpoint!);

    public static string[] FilterAllowedScenarioIds(IEnumerable<string>? allowedScenarioIds)
    {
        return (allowedScenarioIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Where(Scenarios.ContainsKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal sealed record SandboxTrialScenario(
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
}
