using AICopilot.SharedKernel.Result;

namespace AICopilot.Services.Contracts;

public interface ITransactionalExecutionService
{
    Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);

    Task<Result<TValue>> ExecuteResultAsync<TValue>(
        Func<CancellationToken, Task<Result<TValue>>> operation,
        CancellationToken cancellationToken = default);

    Task<Result> ExecuteResultAsync(
        Func<CancellationToken, Task<Result>> operation,
        CancellationToken cancellationToken = default);
}
