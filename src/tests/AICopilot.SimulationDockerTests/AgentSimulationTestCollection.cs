using AICopilot.BackendTests;

namespace AICopilot.SimulationDockerTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AgentSimulationTestCollection : ICollectionFixture<AgentSimulationAICopilotAppFixture>
{
    public const string Name = "AICopilotAgentSimulation";
}
