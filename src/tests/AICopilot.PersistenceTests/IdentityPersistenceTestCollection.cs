using AICopilot.BackendTests;

namespace AICopilot.PersistenceTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class IdentityPersistenceTestCollection : ICollectionFixture<PostgresPersistenceFixture>
{
    public const string Name = "AICopilotIdentityPersistence";
}
