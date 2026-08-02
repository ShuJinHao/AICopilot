using AICopilot.AspireIntegrationTestKit;

namespace AICopilot.EndToEndTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BackendTestCollection : ICollectionFixture<AICopilotAppFixture>
{
    public const string Name = "AICopilotEndToEndFullRuntime";
}
