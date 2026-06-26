using AICopilot.SharedKernel.Ai;

namespace AICopilot.HttpApi.Controllers;

public sealed record UpdateToolRegistrationRequest(
    string? DisplayName = null,
    string? Description = null,
    string? InputSchemaJson = null,
    string? OutputSchemaJson = null,
    AiToolRiskLevel? RiskLevel = null,
    string? RequiredPermission = null,
    bool? RequiresApproval = null,
    bool? IsEnabled = null,
    int? TimeoutSeconds = null,
    string? AuditLevel = null,
    string? Category = null,
    IReadOnlyCollection<string>? BusinessDomains = null,
    string? DataBoundary = null,
    bool? IsVisibleToPlanner = null,
    bool? IsExecutableByAgent = null,
    int? SchemaVersion = null,
    int? CatalogVersion = null,
    string? ApprovalPolicy = null);
