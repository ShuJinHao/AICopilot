using AICopilot.AspireIntegrationTestKit;

namespace AICopilot.EndToEndTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BackendTestCollection : ICollectionFixture<AICopilotAppFixture>
{
    public const string Name = "AICopilotEndToEndFullRuntime";
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CoreBackendTestCollection : ICollectionFixture<CoreAICopilotAppFixture>
{
    public const string Name = "AICopilotEndToEndCoreRuntime";
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CloudSemanticSimulationBackendTestCollection
    : ICollectionFixture<CloudSemanticSimulationAICopilotAppFixture>
{
    public const string Name = "AICopilotEndToEndCloudSemanticRuntime";
}
