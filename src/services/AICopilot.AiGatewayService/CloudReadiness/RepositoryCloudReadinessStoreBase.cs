namespace AICopilot.AiGatewayService.CloudReadiness;

internal abstract class RepositoryCloudReadinessStoreBase
{
    protected static void Execute(Func<Task> action) => action().GetAwaiter().GetResult();

    protected static T Execute<T>(Func<Task<T>> action) => action().GetAwaiter().GetResult();
}
