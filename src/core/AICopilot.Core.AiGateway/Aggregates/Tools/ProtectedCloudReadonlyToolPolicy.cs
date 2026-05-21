using System.Collections.Frozen;

namespace AICopilot.Core.AiGateway.Aggregates.Tools;

public static class ProtectedCloudReadonlyToolPolicy
{
    public const string ProductionToolCode = "query_cloud_data_readonly";
    public const string PilotReadinessToolCode = "query_cloud_pilot_readiness_readonly";
    public const string ProductionPilotToolCode = "query_cloud_production_pilot_readonly";
    public const string ProductionControlledToolCode = "query_cloud_production_controlled_readonly";

    public static readonly FrozenSet<string> ProtectedToolCodes =
        new[] { ProductionToolCode, PilotReadinessToolCode, ProductionPilotToolCode, ProductionControlledToolCode }
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsProtected(string? toolCode)
    {
        return !string.IsNullOrWhiteSpace(toolCode) && ProtectedToolCodes.Contains(toolCode);
    }

    public static ToolRegistrationSeed? GetDefinition(string toolCode)
    {
        return BuiltInToolRegistrations.AgentRuntimeTools.FirstOrDefault(
            definition => string.Equals(definition.ToolCode, toolCode, StringComparison.OrdinalIgnoreCase));
    }

    public static void ForceDisabled(ToolRegistration tool, DateTimeOffset nowUtc)
    {
        var definition = GetDefinition(tool.ToolCode);
        if (definition is null || !IsProtected(definition.ToolCode))
        {
            return;
        }

        tool.Update(
            definition.DisplayName,
            definition.Description,
            definition.ProviderType,
            definition.TargetType,
            definition.TargetName,
            definition.InputSchemaJson,
            definition.OutputSchemaJson,
            definition.RiskLevel,
            definition.RequiredPermission,
            definition.RequiresApproval,
            isEnabled: false,
            definition.TimeoutSeconds,
            definition.AuditLevel,
            nowUtc,
            definition.Category,
            definition.BusinessDomains,
            definition.DataBoundary,
            isVisibleToPlanner: false,
            isExecutableByAgent: false,
            definition.SchemaVersion,
            definition.CatalogVersion,
            definition.ApprovalPolicy);
    }

    public static string? ValidateSafeState(
        string toolCode,
        bool isEnabled,
        bool isVisibleToPlanner,
        bool isExecutableByAgent,
        string? approvalPolicy)
    {
        if (!IsProtected(toolCode))
        {
            return null;
        }

        var definition = GetDefinition(toolCode);
        if (definition is null)
        {
            return $"{toolCode} is a protected CloudReadonly tool but has no built-in definition.";
        }

        if (isEnabled || isVisibleToPlanner || isExecutableByAgent)
        {
            return $"{toolCode} is protected and must remain disabled, hidden, and non-executable.";
        }

        if (!string.Equals(approvalPolicy, definition.ApprovalPolicy, StringComparison.Ordinal))
        {
            return $"{toolCode} is protected and must keep approval policy {definition.ApprovalPolicy}.";
        }

        return null;
    }

    public static bool IsPersistedToolSafe(ToolRegistration tool)
    {
        return ValidateSafeState(
            tool.ToolCode,
            tool.IsEnabled,
            tool.IsVisibleToPlanner,
            tool.IsExecutableByAgent,
            tool.ApprovalPolicy) is null;
    }
}
