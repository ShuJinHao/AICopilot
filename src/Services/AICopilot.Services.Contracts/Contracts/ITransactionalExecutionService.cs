namespace AICopilot.Services.Contracts;

public interface ITransactionalExecutionService
{
    Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);
}
