using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Specification;

namespace AICopilot.Core.AiGateway.Specifications.RoutingModel;

public sealed class RoutingModelConfigurationByIdSpec : Specification<Aggregates.RoutingModel.RoutingModelConfiguration>
{
    public RoutingModelConfigurationByIdSpec(RoutingModelConfigurationId id)
    {
        FilterCondition = configuration => configuration.Id == id;
    }
}

public sealed class ActiveRoutingModelConfigurationSpec : Specification<Aggregates.RoutingModel.RoutingModelConfiguration>
{
    public ActiveRoutingModelConfigurationSpec()
    {
        FilterCondition = configuration => configuration.IsActive;
    }
}

public sealed class RoutingModelConfigurationsOrderedSpec : Specification<Aggregates.RoutingModel.RoutingModelConfiguration>
{
    public RoutingModelConfigurationsOrderedSpec()
    {
        SetOrderBy(configuration => configuration.Name);
    }
}
