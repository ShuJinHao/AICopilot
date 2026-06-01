using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AgentPlugin;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Tools;

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

        if (!ToolRegistryUpdatePolicy.TryParseDataBoundary(request.DataBoundary, out var dataBoundary))
        {
            return Result.Invalid("DataBoundary is invalid.");
        }

        var safetyError = ProtectedCloudReadonlyToolPolicy.ValidateSafeState(
            tool.ToolCode,
            request.IsEnabled ?? tool.IsEnabled,
            request.IsVisibleToPlanner ?? tool.IsVisibleToPlanner,
            request.IsExecutableByAgent ?? tool.IsExecutableByAgent,
            request.ApprovalPolicy ?? tool.ApprovalPolicy);
        if (safetyError is not null)
        {
            return Result.Invalid(safetyError);
        }

        var changedFields = ToolRegistryUpdatePolicy.BuildChangedFields(tool, request, auditLevel, dataBoundary);
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

        if (ProtectedCloudReadonlyToolPolicy.IsProtected(tool.ToolCode))
        {
            ProtectedCloudReadonlyToolPolicy.ForceDisabled(tool, DateTimeOffset.UtcNow);
        }

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

        var safetyError = ProtectedCloudReadonlyToolPolicy.ValidateSafeState(
            request.ToolCode,
            request.IsEnabled,
            request.IsVisibleToPlanner,
            request.IsExecutableByAgent,
            request.ApprovalPolicy);
        if (safetyError is not null)
        {
            return Result.Invalid(safetyError);
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
            if (ProtectedCloudReadonlyToolPolicy.IsProtected(tool.ToolCode))
            {
                ProtectedCloudReadonlyToolPolicy.ForceDisabled(tool, now);
            }
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
            if (ProtectedCloudReadonlyToolPolicy.IsProtected(tool.ToolCode))
            {
                ProtectedCloudReadonlyToolPolicy.ForceDisabled(tool, now);
            }
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
