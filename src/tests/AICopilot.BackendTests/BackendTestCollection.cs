namespace AICopilot.BackendTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BackendTestCollection : ICollectionFixture<AICopilotAppFixture>
{
    public const string Name = "AICopilotBackend";
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CoreBackendTestCollection : ICollectionFixture<CoreAICopilotAppFixture>
{
    public const string Name = "AICopilotCoreBackend";
}
