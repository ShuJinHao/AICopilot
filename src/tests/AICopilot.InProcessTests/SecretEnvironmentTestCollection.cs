namespace AICopilot.InProcessTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SecretEnvironmentTestCollection
{
    public const string Name = "AICopilotSecretEnvironment";
}
