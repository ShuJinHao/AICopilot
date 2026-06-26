using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.CloudReadiness;

internal static class CloudReadonlyPilotReadinessPolicy
{
    public static void ValidateProductionBoundary(
        CloudReadonlyOptions cloudReadonly,
        CloudAiReadOptions cloudAiRead,
        ICollection<string> blockers,
        ICollection<string> warnings,
        IReadOnlyCollection<ToolRegistration>? persistedToolRegistrations)
    {
        if (cloudReadonly.Mode != CloudReadonlyDataSourceMode.Disabled)
        {
            blockers.Add("CloudReadonly.Mode must remain Disabled during P11 Pilot readiness rehearsal.");
        }

        if (cloudReadonly.Real.Enabled)
        {
            blockers.Add("CloudReadonly.Real.Enabled must remain false during P11 Pilot readiness rehearsal.");
        }

        if (cloudReadonly.Real.AllowProductionRead)
        {
            blockers.Add("CloudReadonly.Real.AllowProductionRead must remain false during P11 Pilot readiness rehearsal.");
        }

        if (cloudAiRead.Enabled)
        {
            blockers.Add("CloudAiRead.Enabled must remain false during P11 Pilot readiness rehearsal.");
        }

        if (!string.IsNullOrWhiteSpace(cloudAiRead.ServiceAccountToken))
        {
            warnings.Add("CloudAiRead token is configured but P11 readiness APIs never return token values and never execute real production reads.");
        }

        ValidateBuiltInProtectedTool(ProtectedCloudReadonlyToolPolicy.ProductionToolCode, blockers);
        ValidateBuiltInProtectedTool(ProtectedCloudReadonlyToolPolicy.PilotReadinessToolCode, blockers);
        ValidatePersistedProtectedTools(persistedToolRegistrations, blockers, warnings);
    }

    public static string[] NormalizeAllowedEndpointCodes(IEnumerable<string> endpointCodes)
    {
        return endpointCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Where(CloudReadonlyPilotReadinessContractRehearsal.IsAllowedEndpointCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string NormalizeText(string? value, string fallback, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return normalized.Length > maxLength ? normalized[..maxLength] : normalized;
    }

    private static void ValidateBuiltInProtectedTool(string toolCode, ICollection<string> blockers)
    {
        var tool = BuiltInToolRegistrations.AgentRuntimeTools
            .FirstOrDefault(item => string.Equals(item.ToolCode, toolCode, StringComparison.OrdinalIgnoreCase));
        if (tool is null)
        {
            blockers.Add($"Tool Registry built-in definition is missing {toolCode}.");
            return;
        }

        var safetyError = ProtectedCloudReadonlyToolPolicy.ValidateSafeState(
            tool.ToolCode,
            tool.IsEnabled,
            tool.IsVisibleToPlanner,
            tool.IsExecutableByAgent,
            tool.ApprovalPolicy);
        if (safetyError is not null)
        {
            blockers.Add($"Built-in {safetyError}");
        }

        if (string.Equals(toolCode, ProtectedCloudReadonlyToolPolicy.PilotReadinessToolCode, StringComparison.OrdinalIgnoreCase) &&
            tool.DataBoundary != ToolDataBoundary.CloudReadonlyPilotReadinessOnly)
        {
            blockers.Add($"{toolCode} must use the CloudReadonlyPilotReadinessOnly boundary descriptor.");
        }
    }

    private static void ValidatePersistedProtectedTools(
        IReadOnlyCollection<ToolRegistration>? persistedToolRegistrations,
        ICollection<string> blockers,
        ICollection<string> warnings)
    {
        if (persistedToolRegistrations is null)
        {
            warnings.Add("P11 Pilot readiness did not receive persisted ToolRegistry state; only built-in definitions were checked.");
            return;
        }

        foreach (var toolCode in ProtectedCloudReadonlyToolPolicy.ProtectedToolCodes)
        {
            var persistedTool = persistedToolRegistrations.FirstOrDefault(
                tool => string.Equals(tool.ToolCode, toolCode, StringComparison.OrdinalIgnoreCase));
            if (persistedTool is null)
            {
                blockers.Add($"Persisted ToolRegistry is missing protected tool {toolCode}.");
                continue;
            }

            var safetyError = ProtectedCloudReadonlyToolPolicy.ValidateSafeState(
                persistedTool.ToolCode,
                persistedTool.IsEnabled,
                persistedTool.IsVisibleToPlanner,
                persistedTool.IsExecutableByAgent,
                persistedTool.ApprovalPolicy);
            if (safetyError is not null)
            {
                blockers.Add($"Persisted ToolRegistry unsafe: {safetyError}");
            }
        }
    }
}
