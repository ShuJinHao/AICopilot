using AICopilot.PersistenceTestKit;

namespace AICopilot.PersistenceTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresPersistenceTestCollection : ICollectionFixture<PostgresPersistenceFixture>
{
    public const string Name = "AICopilotPostgresPersistence";
}
