using AICopilot.AiGatewayService.AgentTasks;
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
            cancellationToken,
            request.SkillCode);
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
