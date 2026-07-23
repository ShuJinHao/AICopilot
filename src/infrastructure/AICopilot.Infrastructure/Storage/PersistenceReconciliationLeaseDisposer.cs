using AICopilot.Services.Contracts;
using Microsoft.Extensions.Logging;

namespace AICopilot.Infrastructure.Storage;

internal static class PersistenceReconciliationLeaseDisposer
{
    public static async Task DisposeBestEffortAsync(
        IPersistenceFileReconciliationLease? lease,
        Guid commitId,
        string scope,
        ILogger logger)
    {
        if (lease is null)
        {
            return;
        }

        try
        {
            await lease.DisposeAsync();
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Persistence reconciliation lease cleanup failed. Scope={Scope}; CommitId={CommitId}; ErrorType={ErrorType}",
                scope,
                commitId,
                exception.GetType().Name);
        }
    }
}
