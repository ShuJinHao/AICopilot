using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Tools;

public sealed record ToolRegistrationDto(
    Guid Id,
    string ToolCode,
    string DisplayName,
    string Description,
    string ProviderType,
    string TargetType,
    string TargetName,
    string InputSchemaJson,
    string OutputSchemaJson,
    string RiskLevel,
    string? RequiredPermission,
    bool RequiresApproval,
    bool IsEnabled,
    int TimeoutSeconds,
    string AuditLevel,
    string Category,
    IReadOnlyCollection<string> BusinessDomains,
    string DataBoundary,
    bool IsVisibleToPlanner,
    bool IsExecutableByAgent,
    int SchemaVersion,
    int CatalogVersion,
    string ApprovalPolicy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool RuntimeAvailable,
    DateTimeOffset? LastDiscoveredAt,
    string? SourceServerName);

public sealed record ToolRegistryCatalogDto(
    int Version,
    int AvailableToolCount,
    bool MockMcpOnly,
    IReadOnlyDictionary<string, int> RiskSummary,
    IReadOnlyCollection<AgentPlannerToolSummary> Tools);

public sealed record ToolRunAuditDto(
    Guid ToolRunId,
    Guid TaskId,
    Guid? PlanId,
    string ToolCode,
    string ProviderKind,
    bool IsMock,
    string ApprovalStatus,
    string Status,
    long? DurationMs,
    string? ResultHash,
    string? ErrorCode,
    DateTimeOffset ExecutedAt);

public sealed record ToolExecutionRecordDto(
    Guid Id,
    Guid TaskId,
    Guid StepId,
    Guid? RunAttemptId,
    string ToolCode,
    string? InputSummary,
    string? OutputSummary,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    long? DurationMs,
    string? ErrorCode,
    string? ErrorMessage,
    string? ArtifactId,
    string? AuditMetadata,
    string ProviderKind = "Unknown",
    bool IsMock = false,
    string? ApprovalStatus = null,
    string? ResultHash = null);

public sealed record ToolExecutionRecordPageDto(
    IReadOnlyCollection<ToolExecutionRecordDto> Items,
    int PageIndex,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasPrevious,
    bool HasNext);

[AuthorizeRequirement("AiGateway.ToolRegistry.Read")]
public sealed record GetListToolRegistrationsQuery : IQuery<Result<IReadOnlyCollection<ToolRegistrationDto>>>;

[AuthorizeRequirement("AiGateway.ToolRegistry.Read")]
public sealed record GetToolRegistrationQuery(string ToolCode) : IQuery<Result<ToolRegistrationDto>>;

[AuthorizeRequirement("AiGateway.ToolRegistry.Read")]
public sealed record GetToolCatalogQuery(
    bool SimulationOnly = true,
    IReadOnlyCollection<string>? BusinessDomains = null) : IQuery<Result<ToolRegistryCatalogDto>>;

[AuthorizeRequirement("AiGateway.ToolRegistry.Manage")]
public sealed record UpdateToolRegistrationCommand(
    string ToolCode,
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
    string? ApprovalPolicy = null) : ICommand<Result<ToolRegistrationDto>>;

[AuthorizeRequirement("AiGateway.ToolRegistry.Manage")]
public sealed record UpsertToolDefinitionCommand(
    string ToolCode,
    string DisplayName,
    string Description,
    ToolProviderType ProviderType,
    ToolRegistrationTargetType TargetType,
    string TargetName,
    string InputSchemaJson,
    string OutputSchemaJson,
    AiToolRiskLevel RiskLevel,
    string? RequiredPermission = null,
    bool RequiresApproval = false,
    bool IsEnabled = true,
    int TimeoutSeconds = 120,
    string AuditLevel = "Standard",
    string Category = "General",
    IReadOnlyCollection<string>? BusinessDomains = null,
    string DataBoundary = nameof(ToolDataBoundary.NoData),
    bool IsVisibleToPlanner = true,
    bool IsExecutableByAgent = true,
    int SchemaVersion = 1,
    int CatalogVersion = BuiltInToolRegistrations.CurrentCatalogVersion,
    string ApprovalPolicy = "None") : ICommand<Result<ToolRegistrationDto>>;

[AuthorizeRequirement("AiGateway.ToolRegistry.Manage")]
public sealed record ActivateToolDefinitionVersionCommand(
    string ToolCode,
    int? CatalogVersion = null,
    int? SchemaVersion = null) : ICommand<Result<ToolRegistrationDto>>;

[AuthorizeRequirement("AiGateway.ToolRegistry.Manage")]
public sealed record DisableToolDefinitionCommand(string ToolCode) : ICommand<Result<ToolRegistrationDto>>;
