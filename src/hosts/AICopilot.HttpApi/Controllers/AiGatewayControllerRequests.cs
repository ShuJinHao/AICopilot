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

public sealed record DeadLetterAgentRunQueueItemRequest(string? Reason = null);

public sealed record UpdateTrialCampaignStatusRequest(string Status);

public sealed record AttachAgentTaskToTrialCampaignRequest(
    Guid TaskId,
    string? ScenarioId = null,
    string? TrialMode = null);

public sealed record UpsertTrialRiskIssueRequest(
    Guid? IssueId,
    string Severity,
    string Category,
    string Status,
    string? Owner = null,
    string? SourceRef = null,
    string? ResolutionHash = null);
