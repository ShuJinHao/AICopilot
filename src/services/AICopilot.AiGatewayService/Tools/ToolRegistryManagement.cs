using System.Text.RegularExpressions;
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
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool RuntimeAvailable,
    DateTimeOffset? LastDiscoveredAt,
    string? SourceServerName);

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
    string? AuditMetadata);

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
    string? AuditLevel = null) : ICommand<Result<ToolRegistrationDto>>;

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

        var changedFields = BuildChangedFields(tool, request, auditLevel);
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
            DateTimeOffset.UtcNow);

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
        ToolAuditLevel nextAuditLevel)
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

        return fields;

        void AddIfChanged(string? next, string? current, string field)
        {
            if (next is not null && !string.Equals(next, current, StringComparison.Ordinal))
            {
                fields.Add(field);
            }
        }
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
            tool.CreatedAt,
            tool.UpdatedAt,
            runtimeAvailable,
            tool.ProviderType == ToolProviderType.Mcp ? tool.UpdatedAt : null,
            tool.ProviderType == ToolProviderType.Mcp ? tool.TargetName : null);
    }

    public static ToolExecutionRecordDto Map(ToolExecutionRecord record)
    {
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
            ToolExecutionRecordSanitizer.Sanitize(record.AuditMetadata, 4000));
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
