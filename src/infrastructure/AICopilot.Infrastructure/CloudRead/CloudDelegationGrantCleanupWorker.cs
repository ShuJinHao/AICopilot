using AICopilot.Services.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AICopilot.Infrastructure.CloudRead;

public sealed class CloudDelegationGrantCleanupWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<CloudDelegationGrantCleanupWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    public const int BatchSize = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunOnceSafelyAsync(stoppingToken);
        using var timer = new PeriodicTimer(Interval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunOnceSafelyAsync(stoppingToken);
        }
    }

    public async Task<CloudDelegationPurgeResult> RunOnceAsync(
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var transactionalExecutionService = scope.ServiceProvider
            .GetRequiredService<ITransactionalExecutionService>();
        var grantStore = scope.ServiceProvider
            .GetRequiredService<ICloudDelegationGrantStore>();

        return await transactionalExecutionService.ExecuteAsync(
            ct => grantStore.PurgeExpiredAsync(
                timeProvider.GetUtcNow().UtcDateTime,
                BatchSize,
                ct),
            cancellationToken);
    }

    private async Task RunOnceSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await RunOnceAsync(cancellationToken);
            if (!result.LockAcquired)
            {
                logger.LogDebug(
                    "Cloud delegation cleanup skipped because another instance owns the database lease.");
                return;
            }

            logger.LogInformation(
                "Cloud delegation cleanup completed. ClearedTokens={ClearedTokens}; DeletedMetadata={DeletedMetadata}; BatchSize={BatchSize}",
                result.ClearedTokenCount,
                result.DeletedMetadataCount,
                BatchSize);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Cloud delegation cleanup failed closed. ErrorType={ErrorType}; OriginalMessage=hidden_by_security_policy",
                exception.GetType().Name);
        }
    }
}
