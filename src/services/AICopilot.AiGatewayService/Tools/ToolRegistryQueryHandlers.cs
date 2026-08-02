using AICopilot.AgentPlugin;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Tools;

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
    IReadRepository<ToolRegistration> repository,
    IAgentPluginCatalog pluginCatalog)
    : IQueryHandler<GetToolCatalogQuery, Result<ToolRegistryCatalogDto>>
{
    public async Task<Result<ToolRegistryCatalogDto>> Handle(
        GetToolCatalogQuery request,
        CancellationToken cancellationToken)
    {
        var requestedDomains = (request.BusinessDomains ?? [])
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Select(domain => domain.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var registrations = await repository.ListAsync(cancellationToken: cancellationToken);
        var tools = registrations
            .Where(tool =>
                tool.IsEnabled &&
                tool.IsExecutableByAgent &&
                tool.RiskLevel is not AiToolRiskLevel.Blocked and not AiToolRiskLevel.Critical &&
                (requestedDomains.Count == 0 ||
                 tool.BusinessDomains.Any(requestedDomains.Contains)))
            .OrderBy(tool => tool.ToolCode, StringComparer.OrdinalIgnoreCase)
            .Select(tool => ToolRegistrationMapper.Map(tool, pluginCatalog))
            .Where(tool => tool.RuntimeAvailable)
            .ToArray();
        var riskSummary = tools
            .GroupBy(tool => tool.RiskLevel, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        return Result.Success(new ToolRegistryCatalogDto(
            tools.Select(tool => tool.CatalogVersion).DefaultIfEmpty(0).Max(),
            tools.Length,
            riskSummary,
            tools));
    }
}
