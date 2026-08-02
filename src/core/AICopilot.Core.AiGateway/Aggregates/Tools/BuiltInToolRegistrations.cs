using AICopilot.SharedKernel.Ai;

namespace AICopilot.Core.AiGateway.Aggregates.Tools;

public sealed record ToolRegistrationSeed(
    string ToolCode,
    string DisplayName,
    string Description,
    ToolProviderType ProviderType,
    ToolRegistrationTargetType TargetType,
    string TargetName,
    string InputSchemaJson,
    string OutputSchemaJson,
    AiToolRiskLevel RiskLevel,
    string? RequiredPermission,
    bool RequiresApproval,
    bool IsEnabled,
    int TimeoutSeconds,
    ToolAuditLevel AuditLevel,
    string Category = "General",
    IReadOnlyCollection<string>? BusinessDomains = null,
    ToolDataBoundary DataBoundary = ToolDataBoundary.NoData,
    bool IsExecutableByAgent = true,
    int SchemaVersion = 1,
    int CatalogVersion = BuiltInToolRegistrations.CurrentCatalogVersion);

public static class BuiltInToolRegistrations
{
    public const int CurrentCatalogVersion = 21;
    public const int CurrentSchemaVersion = 3;

    public static IReadOnlyCollection<ToolRegistrationSeed> HarnessTools { get; } =
    [
        new ToolRegistrationSeed(
            "plugin__diagnosticadvisorplugin__generatediagnosticchecklist",
            "设备诊断清单",
            "Generate a read-only diagnostic checklist for human review without executing control actions.",
            ToolProviderType.BuiltIn,
            ToolRegistrationTargetType.Plugin,
            "DiagnosticAdvisorPlugin",
            """{"type":"object","properties":{"issueSummary":{"type":"string"}},"required":["issueSummary"],"additionalProperties":false}""",
            """{"type":"object","properties":{"status":{"type":"string","enum":["advisory"]},"checklist":{"type":"array","items":{"type":"string"}}},"required":["status","checklist"],"additionalProperties":false}""",
            AiToolRiskLevel.RequiresApproval,
            "AiGateway.Chat",
            RequiresApproval: true,
            IsEnabled: true,
            TimeoutSeconds: 30,
            ToolAuditLevel.Standard,
            Category: "Diagnostics",
            BusinessDomains: ["Production", "Maintenance"],
            DataBoundary: ToolDataBoundary.NoData,
            IsExecutableByAgent: true,
            SchemaVersion: CurrentSchemaVersion,
            CatalogVersion: CurrentCatalogVersion)
    ];

    public static ToolRegistrationSeed? FindHarnessTool(string? toolCode) =>
        string.IsNullOrWhiteSpace(toolCode)
            ? null
            : HarnessTools.FirstOrDefault(tool =>
                string.Equals(tool.ToolCode, toolCode, StringComparison.Ordinal));
}
