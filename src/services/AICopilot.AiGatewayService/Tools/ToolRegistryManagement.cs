using System.Text.RegularExpressions;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AgentPlugin;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
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

public sealed class GetListToolRegistrationsQueryHandler(
    IReadRepository<ToolRegistration> repository,
    IAgentPluginCatalog pluginCatalog)
    : IQueryHandler<GetListToolRegistrationsQuery, Result<IReadOnlyCollection<ToolRegistrationDto>>>
{
    public async Task<Result<IReadOnlyCollection<ToolRegistrationDto>>> Handle(
        GetListToolRegistrationsQuery request,
        CancellationToken cancellationToken)
    {
        var tools = await repository.ListAsync(cancellationToken: cancellationToken);
        return Result.Success<IReadOnlyCollection<ToolRegistrationDto>>(
            tools
                .OrderBy(tool => tool.ProviderType)
                .ThenBy(tool => tool.ToolCode, StringComparer.OrdinalIgnoreCase)
                .Select(tool => ToolRegistrationMapper.Map(tool, pluginCatalog))
                .ToArray());
    }
}

public sealed class GetToolRegistrationQueryHandler(
    IReadRepository<ToolRegistration> repository,
    IAgentPluginCatalog pluginCatalog)
    : IQueryHandler<GetToolRegistrationQuery, Result<ToolRegistrationDto>>
{
    public async Task<Result<ToolRegistrationDto>> Handle(
        GetToolRegistrationQuery request,
        CancellationToken cancellationToken)
    {
        var tool = await repository.GetAsync(
            item => item.ToolCode == request.ToolCode,
            cancellationToken: cancellationToken);
        return tool is null
            ? Result.NotFound()
            : Result.Success(ToolRegistrationMapper.Map(tool, pluginCatalog));
    }
}

public sealed class GetToolCatalogQueryHandler(
    AgentPlanToolGuard toolGuard,
    ICurrentUser currentUser)
    : IQueryHandler<GetToolCatalogQuery, Result<ToolRegistryCatalogDto>>
{
    public async Task<Result<ToolRegistryCatalogDto>> Handle(
        GetToolCatalogQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } userId)
        {
            return Result.Unauthorized(new ApiProblemDescriptor(
                AuthProblemCodes.Unauthorized,
                "Current user id is missing or invalid."));
        }

        var catalog = await toolGuard.GetAvailableToolCatalogAsync(
            userId,
            request.SimulationOnly,
            request.BusinessDomains,
            cancellationToken);
        if (!catalog.IsSuccess || catalog.Value is null)
        {
            return Result.From(catalog);
        }

        var riskSummary = catalog.Value.Tools
            .GroupBy(tool => tool.RiskLevel, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        return Result.Success(new ToolRegistryCatalogDto(
            catalog.Value.Version,
            catalog.Value.AvailableToolCount,
            MockMcpOnly: true,
            riskSummary,
            catalog.Value.Tools));
    }
}

public sealed class UpdateToolRegistrationCommandHandler(
    IRepository<ToolRegistration> repository,
    IAgentPluginCatalog pluginCatalog,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<UpdateToolRegistrationCommand, Result<ToolRegistrationDto>>
{
    public async Task<Result<ToolRegistrationDto>> Handle(
        UpdateToolRegistrationCommand request,
        CancellationToken cancellationToken)
    {
        var tool = await repository.GetAsync(
            item => item.ToolCode == request.ToolCode,
            cancellationToken: cancellationToken);
        if (tool is null)
        {
            return Result.NotFound();
        }

        var auditLevel = tool.AuditLevel;
        if (request.AuditLevel is not null &&
            !Enum.TryParse(request.AuditLevel, ignoreCase: true, out auditLevel))
        {
            return Result.Invalid("AuditLevel is invalid.");
        }

        if (!TryParseDataBoundary(request.DataBoundary, out var dataBoundary))
        {
            return Result.Invalid("DataBoundary is invalid.");
        }

        var changedFields = BuildChangedFields(tool, request, auditLevel, dataBoundary);
        tool.Update(
            request.DisplayName ?? tool.DisplayName,
            request.Description ?? tool.Description,
            tool.ProviderType,
            tool.TargetType,
            tool.TargetName,
            request.InputSchemaJson ?? tool.InputSchemaJson,
            request.OutputSchemaJson ?? tool.OutputSchemaJson,
            request.RiskLevel ?? tool.RiskLevel,
            request.RequiredPermission ?? tool.RequiredPermission,
            request.RequiresApproval ?? tool.RequiresApproval,
            request.IsEnabled ?? tool.IsEnabled,
            request.TimeoutSeconds ?? tool.TimeoutSeconds,
            auditLevel,
            DateTimeOffset.UtcNow,
            request.Category ?? tool.Category,
            request.BusinessDomains ?? tool.BusinessDomains,
            dataBoundary ?? tool.DataBoundary,
            request.IsVisibleToPlanner ?? tool.IsVisibleToPlanner,
            request.IsExecutableByAgent ?? tool.IsExecutableByAgent,
            request.SchemaVersion ?? tool.SchemaVersion,
            request.CatalogVersion ?? tool.CatalogVersion,
            request.ApprovalPolicy ?? tool.ApprovalPolicy);

        repository.Update(tool);
        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Config,
                "AiGateway.UpdateToolRegistration",
                "ToolRegistration",
                tool.Id.Value.ToString(),
                tool.ToolCode,
                AuditResults.Succeeded,
                $"Updated tool registration: {tool.ToolCode}; changed={(changedFields.Count == 0 ? "none" : string.Join(", ", changedFields))}; enabled={tool.IsEnabled}; risk={tool.RiskLevel}; requiresApproval={tool.RequiresApproval}.",
                changedFields),
            cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success(ToolRegistrationMapper.Map(tool, pluginCatalog));
    }

    private static IReadOnlyCollection<string> BuildChangedFields(
        ToolRegistration tool,
        UpdateToolRegistrationCommand request,
        ToolAuditLevel nextAuditLevel,
        ToolDataBoundary? nextDataBoundary)
    {
        var fields = new List<string>();
        AddIfChanged(request.DisplayName, tool.DisplayName, "displayName");
        AddIfChanged(request.Description, tool.Description, "description");
        AddIfChanged(request.InputSchemaJson, tool.InputSchemaJson, "inputSchemaJson");
        AddIfChanged(request.OutputSchemaJson, tool.OutputSchemaJson, "outputSchemaJson");
        if (request.RiskLevel.HasValue && request.RiskLevel.Value != tool.RiskLevel)
        {
            fields.Add("riskLevel");
        }

        AddIfChanged(request.RequiredPermission, tool.RequiredPermission, "requiredPermission");
        if (request.RequiresApproval.HasValue && request.RequiresApproval.Value != tool.RequiresApproval)
        {
            fields.Add("requiresApproval");
        }

        if (request.IsEnabled.HasValue && request.IsEnabled.Value != tool.IsEnabled)
        {
            fields.Add("isEnabled");
        }

        if (request.TimeoutSeconds.HasValue && request.TimeoutSeconds.Value != tool.TimeoutSeconds)
        {
            fields.Add("timeoutSeconds");
        }

        if (request.AuditLevel is not null && nextAuditLevel != tool.AuditLevel)
        {
            fields.Add("auditLevel");
        }

        AddIfChanged(request.Category, tool.Category, "category");
        if (request.BusinessDomains is not null &&
            !request.BusinessDomains.SequenceEqual(tool.BusinessDomains, StringComparer.OrdinalIgnoreCase))
        {
            fields.Add("businessDomains");
        }

        if (nextDataBoundary.HasValue && nextDataBoundary.Value != tool.DataBoundary)
        {
            fields.Add("dataBoundary");
        }

        if (request.IsVisibleToPlanner.HasValue && request.IsVisibleToPlanner.Value != tool.IsVisibleToPlanner)
        {
            fields.Add("isVisibleToPlanner");
        }

        if (request.IsExecutableByAgent.HasValue && request.IsExecutableByAgent.Value != tool.IsExecutableByAgent)
        {
            fields.Add("isExecutableByAgent");
        }

        if (request.SchemaVersion.HasValue && request.SchemaVersion.Value != tool.SchemaVersion)
        {
            fields.Add("schemaVersion");
        }

        if (request.CatalogVersion.HasValue && request.CatalogVersion.Value != tool.CatalogVersion)
        {
            fields.Add("catalogVersion");
        }

        AddIfChanged(request.ApprovalPolicy, tool.ApprovalPolicy, "approvalPolicy");
        return fields;

        void AddIfChanged(string? next, string? current, string field)
        {
            if (next is not null && !string.Equals(next, current, StringComparison.Ordinal))
            {
                fields.Add(field);
            }
        }
    }

    internal static bool TryParseDataBoundary(string? value, out ToolDataBoundary? dataBoundary)
    {
        dataBoundary = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!Enum.TryParse<ToolDataBoundary>(value, ignoreCase: true, out var parsed))
        {
            return false;
        }

        dataBoundary = parsed;
        return true;
    }
}

public sealed class UpsertToolDefinitionCommandHandler(
    IRepository<ToolRegistration> repository,
    IAgentPluginCatalog pluginCatalog,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<UpsertToolDefinitionCommand, Result<ToolRegistrationDto>>
{
    public async Task<Result<ToolRegistrationDto>> Handle(
        UpsertToolDefinitionCommand request,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ToolAuditLevel>(request.AuditLevel, ignoreCase: true, out var auditLevel))
        {
            return Result.Invalid("AuditLevel is invalid.");
        }

        if (!Enum.TryParse<ToolDataBoundary>(request.DataBoundary, ignoreCase: true, out var dataBoundary))
        {
            return Result.Invalid("DataBoundary is invalid.");
        }

        var now = DateTimeOffset.UtcNow;
        var tool = await repository.GetAsync(
            item => item.ToolCode == request.ToolCode,
            cancellationToken: cancellationToken);
        var created = tool is null;
        if (tool is null)
        {
            tool = new ToolRegistration(
                request.ToolCode,
                request.DisplayName,
                request.Description,
                request.ProviderType,
                request.TargetType,
                request.TargetName,
                request.InputSchemaJson,
                request.OutputSchemaJson,
                request.RiskLevel,
                request.RequiredPermission,
                request.RequiresApproval,
                request.IsEnabled,
                request.TimeoutSeconds,
                auditLevel,
                now,
                request.Category,
                request.BusinessDomains,
                dataBoundary,
                request.IsVisibleToPlanner,
                request.IsExecutableByAgent,
                request.SchemaVersion,
                request.CatalogVersion,
                request.ApprovalPolicy);
            repository.Add(tool);
        }
        else
        {
            tool.Update(
                request.DisplayName,
                request.Description,
                request.ProviderType,
                request.TargetType,
                request.TargetName,
                request.InputSchemaJson,
                request.OutputSchemaJson,
                request.RiskLevel,
                request.RequiredPermission,
                request.RequiresApproval,
                request.IsEnabled,
                request.TimeoutSeconds,
                auditLevel,
                now,
                request.Category,
                request.BusinessDomains,
                dataBoundary,
                request.IsVisibleToPlanner,
                request.IsExecutableByAgent,
                request.SchemaVersion,
                request.CatalogVersion,
                request.ApprovalPolicy);
            repository.Update(tool);
        }

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Config,
                created ? "AiGateway.CreateToolDefinition" : "AiGateway.UpsertToolDefinition",
                "ToolRegistration",
                tool.Id.Value.ToString(),
                tool.ToolCode,
                AuditResults.Succeeded,
                $"{(created ? "Created" : "Updated")} tool definition: {tool.ToolCode}; provider={tool.ProviderType}; risk={tool.RiskLevel}; dataBoundary={tool.DataBoundary}; enabled={tool.IsEnabled}.",
                ["toolCode", "providerType", "riskLevel", "dataBoundary", "catalogVersion"]),
            cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success(ToolRegistrationMapper.Map(tool, pluginCatalog));
    }
}

public sealed class ActivateToolDefinitionVersionCommandHandler(
    IRepository<ToolRegistration> repository,
    IAgentPluginCatalog pluginCatalog,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<ActivateToolDefinitionVersionCommand, Result<ToolRegistrationDto>>
{
    public async Task<Result<ToolRegistrationDto>> Handle(
        ActivateToolDefinitionVersionCommand request,
        CancellationToken cancellationToken)
    {
        var tool = await repository.GetAsync(item => item.ToolCode == request.ToolCode, cancellationToken: cancellationToken);
        if (tool is null)
        {
            return Result.NotFound();
        }

        tool.Update(
            tool.DisplayName,
            tool.Description,
            tool.ProviderType,
            tool.TargetType,
            tool.TargetName,
            tool.InputSchemaJson,
            tool.OutputSchemaJson,
            tool.RiskLevel,
            tool.RequiredPermission,
            tool.RequiresApproval,
            true,
            tool.TimeoutSeconds,
            tool.AuditLevel,
            DateTimeOffset.UtcNow,
            tool.Category,
            tool.BusinessDomains,
            tool.DataBoundary,
            tool.IsVisibleToPlanner,
            tool.IsExecutableByAgent,
            request.SchemaVersion ?? tool.SchemaVersion,
            request.CatalogVersion ?? tool.CatalogVersion,
            tool.ApprovalPolicy);
        repository.Update(tool);
        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Config,
                "AiGateway.ActivateToolDefinitionVersion",
                "ToolRegistration",
                tool.Id.Value.ToString(),
                tool.ToolCode,
                AuditResults.Succeeded,
                $"Activated tool definition version: {tool.ToolCode}; catalogVersion={tool.CatalogVersion}; schemaVersion={tool.SchemaVersion}; enabled={tool.IsEnabled}.",
                ["catalogVersion", "schemaVersion", "isEnabled"]),
            cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success(ToolRegistrationMapper.Map(tool, pluginCatalog));
    }
}

public sealed class DisableToolDefinitionCommandHandler(
    IRepository<ToolRegistration> repository,
    IAgentPluginCatalog pluginCatalog,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<DisableToolDefinitionCommand, Result<ToolRegistrationDto>>
{
    public async Task<Result<ToolRegistrationDto>> Handle(
        DisableToolDefinitionCommand request,
        CancellationToken cancellationToken)
    {
        var tool = await repository.GetAsync(item => item.ToolCode == request.ToolCode, cancellationToken: cancellationToken);
        if (tool is null)
        {
            return Result.NotFound();
        }

        tool.Update(
            tool.DisplayName,
            tool.Description,
            tool.ProviderType,
            tool.TargetType,
            tool.TargetName,
            tool.InputSchemaJson,
            tool.OutputSchemaJson,
            tool.RiskLevel,
            tool.RequiredPermission,
            tool.RequiresApproval,
            false,
            tool.TimeoutSeconds,
            tool.AuditLevel,
            DateTimeOffset.UtcNow,
            tool.Category,
            tool.BusinessDomains,
            tool.DataBoundary,
            tool.IsVisibleToPlanner,
            false,
            tool.SchemaVersion,
            tool.CatalogVersion,
            tool.ApprovalPolicy);
        repository.Update(tool);
        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Config,
                "AiGateway.DisableToolDefinition",
                "ToolRegistration",
                tool.Id.Value.ToString(),
                tool.ToolCode,
                AuditResults.Succeeded,
                $"Disabled tool definition: {tool.ToolCode}.",
                ["isEnabled", "isExecutableByAgent"]),
            cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success(ToolRegistrationMapper.Map(tool, pluginCatalog));
    }
}

internal static class ToolRegistrationMapper
{
    public static ToolRegistrationDto Map(ToolRegistration tool, IAgentPluginCatalog? pluginCatalog = null)
    {
        var runtimeAvailable = tool.ProviderType != ToolProviderType.Mcp ||
                               pluginCatalog?.GetAllTools().Any(runtimeTool =>
                                   string.Equals(runtimeTool.Name, tool.ToolCode, StringComparison.OrdinalIgnoreCase)) == true;
        return new ToolRegistrationDto(
            tool.Id.Value,
            tool.ToolCode,
            tool.DisplayName,
            tool.Description,
            tool.ProviderType.ToString(),
            tool.TargetType.ToString(),
            tool.TargetName,
            tool.InputSchemaJson,
            tool.OutputSchemaJson,
            tool.RiskLevel.ToString(),
            tool.RequiredPermission,
            tool.RequiresApproval,
            tool.IsEnabled,
            tool.TimeoutSeconds,
            tool.AuditLevel.ToString(),
            tool.Category,
            tool.BusinessDomains,
            tool.DataBoundary.ToString(),
            tool.IsVisibleToPlanner,
            tool.IsExecutableByAgent,
            tool.SchemaVersion,
            tool.CatalogVersion,
            tool.ApprovalPolicy,
            tool.CreatedAt,
            tool.UpdatedAt,
            runtimeAvailable,
            tool.ProviderType == ToolProviderType.Mcp ? tool.UpdatedAt : null,
            tool.ProviderType == ToolProviderType.Mcp ? tool.TargetName : null);
    }

    public static ToolExecutionRecordDto Map(ToolExecutionRecord record)
    {
        var metadata = ParseMetadata(record.AuditMetadata);
        return new ToolExecutionRecordDto(
            record.Id.Value,
            record.TaskId.Value,
            record.StepId.Value,
            record.RunAttemptId?.Value,
            record.ToolCode,
            ToolExecutionRecordSanitizer.Sanitize(record.InputSummary, 2000),
            ToolExecutionRecordSanitizer.Sanitize(record.OutputSummary, 4000),
            record.Status.ToString(),
            record.StartedAt,
            record.CompletedAt,
            record.DurationMs,
            record.ErrorCode,
            ToolExecutionRecordSanitizer.Sanitize(record.ErrorMessage, 2000),
            record.ArtifactId,
            ToolExecutionRecordSanitizer.Sanitize(record.AuditMetadata, 4000),
            metadata.TryGetValue("providerKind", out var providerKind)
                ? providerKind
                : metadata.TryGetValue("providerType", out var providerType)
                    ? providerType
                    : "Unknown",
            metadata.TryGetValue("isMock", out var isMock) && bool.TryParse(isMock, out var parsedIsMock) && parsedIsMock,
            metadata.TryGetValue("approvalStatus", out var approvalStatus) ? approvalStatus : null,
            metadata.TryGetValue("resultHash", out var resultHash) ? resultHash : null);
    }

    private static IReadOnlyDictionary<string, string> ParseMetadata(string? auditMetadata)
    {
        if (string.IsNullOrWhiteSpace(auditMetadata))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(auditMetadata);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            return document.RootElement
                .EnumerateObject()
                .ToDictionary(
                    property => property.Name,
                    property => property.Value.ValueKind == System.Text.Json.JsonValueKind.String
                        ? property.Value.GetString() ?? string.Empty
                        : property.Value.ToString(),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (System.Text.Json.JsonException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}

internal static partial class ToolExecutionRecordSanitizer
{
    public static string? Sanitize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var sanitized = SecretPattern().Replace(value, "$1=******");
        sanitized = WindowsPathPattern().Replace(sanitized, "[redacted-path]");
        sanitized = ConnectionStringPartPattern().Replace(sanitized, "$1=******");
        sanitized = SqlStatementPattern().Replace(sanitized, "[redacted-sql]");
        sanitized = SqlObjectPattern().Replace(sanitized, "$1 [redacted]");

        return sanitized.Length <= maxLength ? sanitized : sanitized[..maxLength];
    }

    [GeneratedRegex(@"(?i)(api[_-]?key|token|password|secret|connection\s*string)\s*[""':=]+\s*[^,""}\s]+")]
    private static partial Regex SecretPattern();

    [GeneratedRegex(@"[A-Za-z]:\\[^\s""']+")]
    private static partial Regex WindowsPathPattern();

    [GeneratedRegex(@"(?i)(Host|Username|Password|Database|Port)\s*=\s*[^;""'}]+")]
    private static partial Regex ConnectionStringPartPattern();

    [GeneratedRegex(@"(?is)\b(select|insert|update|delete|drop|alter|create)\b\s+.+?(?=,|;|\}|$)")]
    private static partial Regex SqlStatementPattern();

    [GeneratedRegex(@"(?i)\b(from|join|table)\s+[""`\[]?[A-Za-z0-9_.\]-]+")]
    private static partial Regex SqlObjectPattern();
}
