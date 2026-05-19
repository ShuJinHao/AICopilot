using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Specifications.LanguageModel;
using AICopilot.Core.AiGateway.Specifications.RoutingModel;
using AICopilot.SharedKernel.Repository;

namespace AICopilot.AiGatewayService.RoutingModels;

public interface IRoutingModelResolver
{
    Task<LanguageModel?> ResolveActiveModelAsync(CancellationToken cancellationToken = default);
}

public sealed class RoutingModelResolver(
    IReadRepository<Core.AiGateway.Aggregates.RoutingModel.RoutingModelConfiguration> routingRepository,
    IReadRepository<LanguageModel> modelRepository) : IRoutingModelResolver
{
    public async Task<LanguageModel?> ResolveActiveModelAsync(CancellationToken cancellationToken = default)
    {
        var active = await routingRepository.FirstOrDefaultAsync(new ActiveRoutingModelConfigurationSpec(), cancellationToken);
        if (active is null)
        {
            return null;
        }

        var model = await modelRepository.FirstOrDefaultAsync(new LanguageModelByIdSpec(active.ModelId), cancellationToken);
        return model is { IsEnabled: true } && model.SupportsUsage(LanguageModelUsage.Routing)
            ? model
            : null;
    }
}
