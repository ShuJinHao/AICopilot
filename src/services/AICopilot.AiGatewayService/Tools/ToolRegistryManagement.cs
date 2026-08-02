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
    bool IsExecutableByAgent,
    int SchemaVersion,
    int CatalogVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool RuntimeAvailable,
    DateTimeOffset? LastDiscoveredAt,
    string? SourceServerName);

public sealed record ToolRegistryCatalogDto(
    int Version,
    int AvailableToolCount,
    IReadOnlyDictionary<string, int> RiskSummary,
    IReadOnlyCollection<ToolRegistrationDto> Tools);

[AuthorizeRequirement("AiGateway.ToolRegistry.Read")]
public sealed record GetListToolRegistrationsQuery
    : IQuery<Result<IReadOnlyCollection<ToolRegistrationDto>>>;

[AuthorizeRequirement("AiGateway.ToolRegistry.Read")]
public sealed record GetToolRegistrationQuery(string ToolCode)
    : IQuery<Result<ToolRegistrationDto>>;

[AuthorizeRequirement("AiGateway.ToolRegistry.Read")]
public sealed record GetToolCatalogQuery(
    IReadOnlyCollection<string>? BusinessDomains = null)
    : IQuery<Result<ToolRegistryCatalogDto>>;

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
    bool? IsExecutableByAgent = null,
    int? SchemaVersion = null,
    int? CatalogVersion = null) : ICommand<Result<ToolRegistrationDto>>;

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
    bool IsExecutableByAgent = true,
    int SchemaVersion = 1,
    int CatalogVersion = BuiltInToolRegistrations.CurrentCatalogVersion)
    : ICommand<Result<ToolRegistrationDto>>;

[AuthorizeRequirement("AiGateway.ToolRegistry.Manage")]
public sealed record ActivateToolDefinitionVersionCommand(
    string ToolCode,
    int? CatalogVersion = null,
    int? SchemaVersion = null) : ICommand<Result<ToolRegistrationDto>>;

[AuthorizeRequirement("AiGateway.ToolRegistry.Manage")]
public sealed record DisableToolDefinitionCommand(string ToolCode)
    : ICommand<Result<ToolRegistrationDto>>;
