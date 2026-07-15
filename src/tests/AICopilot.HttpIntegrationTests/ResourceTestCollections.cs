using AICopilot.AspireIntegrationTestKit;

namespace AICopilot.HttpIntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BackendTestCollection : ICollectionFixture<AICopilotAppFixture>
{
    public const string Name = "AICopilotHttpFullRuntime";
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CoreBackendTestCollection : ICollectionFixture<CoreAICopilotAppFixture>
{
    public const string Name = "AICopilotHttpCoreRuntime";
}
