using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;

namespace AICopilot.DataAnalysisService.SimulationBusiness;

[AuthorizeRequirement("DataSource.Manage")]
public sealed record GetSimulationBusinessSeedPlanQuery(
    SimulationBusinessProfile Profile = SimulationBusinessProfile.Medium,
    int Seed = 20260519) : IQuery<Result<SimulationBusinessSeedPlan>>;

public sealed class GetSimulationBusinessSeedPlanQueryHandler(
    ISimulationBusinessSeedGenerator seedGenerator)
    : IQueryHandler<GetSimulationBusinessSeedPlanQuery, Result<SimulationBusinessSeedPlan>>
{
    public Task<Result<SimulationBusinessSeedPlan>> Handle(
        GetSimulationBusinessSeedPlanQuery request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success(seedGenerator.CreatePlan(request.Profile, request.Seed)));
    }
}
