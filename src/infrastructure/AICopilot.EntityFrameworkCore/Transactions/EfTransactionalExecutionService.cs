using Microsoft.EntityFrameworkCore;
using AICopilot.Services.Contracts;

namespace AICopilot.EntityFrameworkCore.Transactions;

public sealed class EfTransactionalExecutionService(
    IdentityStoreDbContext dbContext) : ITransactionalExecutionService
{
    public Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();

        return strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var result = await operation(cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }
}
