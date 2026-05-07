using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.Core.McpServer.Ids;
using AICopilot.Core.McpServer.Specifications.McpServerInfo;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.McpService.McpServers;

public record McpToolPolicySummaryDto
{
    public required string ToolName { get; init; }
    public bool RequiresApproval { get; init; }
    public bool RequiresOnsiteAttestation { get; init; }
}

public record McpAllowedToolDto
{
    public required string ToolName { get; init; }
    public AiToolExternalSystemType? ExternalSystemType { get; init; }
    public AiToolCapabilityKind? CapabilityKind { get; init; }
    public AiToolRiskLevel? RiskLevel { get; init; }
    public bool ReadOnlyDeclared { get; init; }
    public bool? McpReadOnlyHint { get; init; }
    public bool? McpDestructiveHint { get; init; }
    public bool? McpIdempotentHint { get; init; }
}

public record McpServerDto
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required McpTransportType TransportType { get; init; }
    public string? Command { get; init; }
    public bool HasArguments { get; init; }
    public string? ArgumentsMasked { get; init; }
    public ChatExposureMode ChatExposureMode { get; init; }
    public IReadOnlyCollection<McpAllowedToolDto> AllowedTools { get; init; } = [];
    public AiToolExternalSystemType ExternalSystemType { get; init; }
    public AiToolCapabilityKind CapabilityKind { get; init; }
    public AiToolRiskLevel RiskLevel { get; init; }
    public IReadOnlyCollection<McpToolPolicySummaryDto> ToolPolicySummaries { get; init; } = [];
    public bool IsEnabled { get; init; }
}

public record CreatedMcpServerDto(Guid Id, string Name);

[AuthorizeRequirement("Mcp.CreateServer")]
public record CreateMcpServerCommand(
    string Name,
    string Description,
    McpTransportType TransportType,
    string? Command,
    string Arguments,
    ChatExposureMode ChatExposureMode = ChatExposureMode.Disabled,
    IReadOnlyCollection<McpAllowedToolDto>? AllowedTools = null,
    bool IsEnabled = true,
    AiToolExternalSystemType ExternalSystemType = AiToolExternalSystemType.Unknown,
    AiToolCapabilityKind CapabilityKind = AiToolCapabilityKind.Diagnostics,
    AiToolRiskLevel RiskLevel = AiToolRiskLevel.RequiresApproval) : ICommand<Result<CreatedMcpServerDto>>;

public class CreateMcpServerCommandHandler(IRepository<McpServerInfo> repository)
    : ICommandHandler<CreateMcpServerCommand, Result<CreatedMcpServerDto>>
{
    public async Task<Result<CreatedMcpServerDto>> Handle(
        CreateMcpServerCommand request,
        CancellationToken cancellationToken)
    {
        var entity = new McpServerInfo(
            request.Name,
            request.Description,
            request.TransportType,
            request.Command,
            request.Arguments,
            request.ChatExposureMode,
            McpAllowedToolMapper.ToDomainTools(request.AllowedTools),
            request.IsEnabled,
            request.ExternalSystemType,
            request.CapabilityKind,
            request.RiskLevel);

        repository.Add(entity);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreatedMcpServerDto(entity.Id, entity.Name));
    }
}

[AuthorizeRequirement("Mcp.UpdateServer")]
public record UpdateMcpServerCommand(
    Guid Id,
    string Name,
    string Description,
    McpTransportType TransportType,
    string? Command,
    string Arguments,
    ChatExposureMode ChatExposureMode = ChatExposureMode.Disabled,
    IReadOnlyCollection<McpAllowedToolDto>? AllowedTools = null,
    bool IsEnabled = true,
    AiToolExternalSystemType ExternalSystemType = AiToolExternalSystemType.Unknown,
    AiToolCapabilityKind CapabilityKind = AiToolCapabilityKind.Diagnostics,
    AiToolRiskLevel RiskLevel = AiToolRiskLevel.RequiresApproval) : ICommand<Result>;

public class UpdateMcpServerCommandHandler(IRepository<McpServerInfo> repository)
    : ICommandHandler<UpdateMcpServerCommand, Result>
{
    public async Task<Result> Handle(UpdateMcpServerCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.GetByIdAsync(new McpServerId(request.Id), cancellationToken);
        if (entity == null)
        {
            return Result.NotFound();
        }

        var arguments = string.IsNullOrWhiteSpace(request.Arguments)
            ? entity.Arguments
            : request.Arguments;

        entity.Update(
            request.Name,
            request.Description,
            request.TransportType,
            request.Command,
            arguments,
            request.ChatExposureMode,
            McpAllowedToolMapper.ToDomainTools(request.AllowedTools),
            request.IsEnabled,
            request.ExternalSystemType,
            request.CapabilityKind,
            request.RiskLevel);

        repository.Update(entity);
        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal static class McpAllowedToolMapper
{
    public static IReadOnlyCollection<McpAllowedTool> ToDomainTools(IReadOnlyCollection<McpAllowedToolDto>? tools)
    {
        return (tools ?? [])
            .Select(tool => new McpAllowedTool(
                tool.ToolName,
                tool.ExternalSystemType,
                tool.CapabilityKind,
                tool.RiskLevel,
                tool.ReadOnlyDeclared,
                tool.McpReadOnlyHint,
                tool.McpDestructiveHint,
                tool.McpIdempotentHint))
            .ToArray();
    }

    public static McpAllowedToolDto ToDto(McpAllowedTool tool)
    {
        return new McpAllowedToolDto
        {
            ToolName = tool.ToolName,
            ExternalSystemType = tool.ExternalSystemType,
            CapabilityKind = tool.CapabilityKind,
            RiskLevel = tool.RiskLevel,
            ReadOnlyDeclared = tool.ReadOnlyDeclared,
            McpReadOnlyHint = tool.McpReadOnlyHint,
            McpDestructiveHint = tool.McpDestructiveHint,
            McpIdempotentHint = tool.McpIdempotentHint
        };
    }
}

[AuthorizeRequirement("Mcp.DeleteServer")]
public record DeleteMcpServerCommand(Guid Id) : ICommand<Result>;

public class DeleteMcpServerCommandHandler(IRepository<McpServerInfo> repository)
    : ICommandHandler<DeleteMcpServerCommand, Result>
{
    public async Task<Result> Handle(DeleteMcpServerCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.GetByIdAsync(new McpServerId(request.Id), cancellationToken);
        if (entity == null)
        {
            return Result.Success();
        }

        repository.Delete(entity);
        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

[AuthorizeRequirement("Mcp.GetServer")]
public record GetMcpServerQuery(Guid Id) : IQuery<Result<McpServerDto>>;

public class GetMcpServerQueryHandler(
    IReadRepository<McpServerInfo> serverRepository,
    IApprovalRequirementReadService approvalRequirementReadService)
    : IQueryHandler<GetMcpServerQuery, Result<McpServerDto>>
{
    public async Task<Result<McpServerDto>> Handle(GetMcpServerQuery request, CancellationToken cancellationToken)
    {
        var server = await serverRepository.FirstOrDefaultAsync(
            new McpServerInfoByIdSpec(new McpServerId(request.Id)),
            cancellationToken);
        if (server == null)
        {
            return Result.NotFound();
        }

        var policies = await LoadPoliciesAsync([server.Name], cancellationToken);
        return Result.Success(McpServerDtoMapper.Map(server, policies));
    }

    private async Task<Dictionary<string, IReadOnlyCollection<ApprovalToolRequirementDto>>> LoadPoliciesAsync(
        IReadOnlyCollection<string> serverNames,
        CancellationToken cancellationToken)
    {
        if (serverNames.Count == 0)
        {
            return [];
        }

        var requirements = await approvalRequirementReadService.GetToolRequirementsAsync(
            AiToolTargetType.McpServer,
            serverNames,
            cancellationToken);

        return requirements
            .GroupBy(requirement => requirement.TargetName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyCollection<ApprovalToolRequirementDto>)group.ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

}

[AuthorizeRequirement("Mcp.GetListServers")]
public record GetListMcpServersQuery : IQuery<Result<IList<McpServerDto>>>;

public class GetListMcpServersQueryHandler(
    IReadRepository<McpServerInfo> serverRepository,
    IApprovalRequirementReadService approvalRequirementReadService)
    : IQueryHandler<GetListMcpServersQuery, Result<IList<McpServerDto>>>
{
    public async Task<Result<IList<McpServerDto>>> Handle(
        GetListMcpServersQuery request,
        CancellationToken cancellationToken)
    {
        var servers = await serverRepository.ListAsync(new McpServerInfosOrderedSpec(), cancellationToken);
        var serverNames = servers.Select(server => server.Name).ToArray();

        var requirements = await approvalRequirementReadService.GetToolRequirementsAsync(
            AiToolTargetType.McpServer,
            serverNames,
            cancellationToken);

        var policiesByTargetName = requirements
            .GroupBy(requirement => requirement.TargetName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyCollection<ApprovalToolRequirementDto>)group.ToArray(),
                StringComparer.OrdinalIgnoreCase);

        IList<McpServerDto> result = servers
            .Select(server => McpServerDtoMapper.Map(server, policiesByTargetName))
            .ToList();
        return Result.Success(result);
    }
}

internal static class McpServerDtoMapper
{
    public static McpServerDto Map(
        McpServerInfo server,
        IReadOnlyDictionary<string, IReadOnlyCollection<ApprovalToolRequirementDto>> policiesByTargetName)
    {
        policiesByTargetName.TryGetValue(server.Name, out var policies);

        return new McpServerDto
        {
            Id = server.Id,
            Name = server.Name,
            Description = server.Description,
            TransportType = server.TransportType,
            Command = server.Command,
            HasArguments = !string.IsNullOrEmpty(server.Arguments),
            ArgumentsMasked = string.IsNullOrEmpty(server.Arguments) ? null : "******",
            ChatExposureMode = server.ChatExposureMode,
            AllowedTools = server.AllowedTools.Select(McpAllowedToolMapper.ToDto).ToArray(),
            ExternalSystemType = server.ExternalSystemType,
            CapabilityKind = server.CapabilityKind,
            RiskLevel = server.RiskLevel,
            ToolPolicySummaries = BuildToolPolicySummaries(server.AllowedTools, policies),
            IsEnabled = server.IsEnabled
        };
    }

    private static IReadOnlyCollection<McpToolPolicySummaryDto> BuildToolPolicySummaries(
        IReadOnlyCollection<McpAllowedTool> allowedTools,
        IReadOnlyCollection<ApprovalToolRequirementDto>? policies)
    {
        if (allowedTools.Count == 0)
        {
            return [];
        }

        var effectivePolicies = policies ?? [];
        return allowedTools
            .Select(tool =>
            {
                var matchedPolicies = effectivePolicies
                    .Where(policy => string.Equals(policy.ToolName, tool.ToolName, StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                return new McpToolPolicySummaryDto
                {
                    ToolName = tool.ToolName,
                    RequiresApproval = matchedPolicies.Any(policy => policy.RequiresApproval),
                    RequiresOnsiteAttestation = matchedPolicies.Any(policy => policy.RequiresOnsiteAttestation)
                };
            })
            .ToArray();
    }
}
