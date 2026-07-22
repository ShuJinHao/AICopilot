using AICopilot.EntityFrameworkCore.Persistence;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Options;

namespace AICopilot.DataWorker;

public sealed class PersistenceMaintenanceOptions
{
    public const string SectionName = "PersistenceMaintenance";

    public int IntervalSeconds { get; set; } = 300;

    public int ReconciliationDelayMinutes { get; set; } = 10;

    public int MarkerRetentionDays { get; set; } = 30;

    public int BatchSize { get; set; } = 100;
}

public sealed class PersistenceMaintenanceWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<PersistenceMaintenanceOptions> options,
    ILogger<PersistenceMaintenanceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var interval = TimeSpan.FromSeconds(settings.IntervalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var maintenance = scope.ServiceProvider
                    .GetRequiredService<PersistenceFileMaintenanceService>();
                var result = await maintenance.RunOnceAsync(
                    DateTime.UtcNow,
                    TimeSpan.FromMinutes(settings.ReconciliationDelayMinutes),
                    TimeSpan.FromDays(settings.MarkerRetentionDays),
                    settings.BatchSize,
                    stoppingToken);
                var artifactFileSetMaintenance = scope.ServiceProvider
                    .GetRequiredService<IArtifactFileSetMaintenanceService>();
                var artifactFileSetResult = await artifactFileSetMaintenance.RunOnceAsync(
                    DateTimeOffset.UtcNow,
                    TimeSpan.FromMinutes(settings.ReconciliationDelayMinutes),
                    settings.BatchSize,
                    stoppingToken);
                var quotaStore = scope.ServiceProvider.GetService<IModelQuotaReservationStore>();
                var reclaimedQuotaReservations = quotaStore is null
                    ? 0
                    : await quotaStore.ReclaimExpiredAsync(
                        DateTimeOffset.UtcNow,
                        settings.BatchSize,
                        stoppingToken);
                logger.LogInformation(
                    "Persistence maintenance completed. CommittedFiles={CommittedFiles}; RolledBackFiles={RolledBackFiles}; FailedFiles={FailedFiles}; ActiveFiles={ActiveFiles}; DeletedMarkers={DeletedMarkers}; MarkerCleanupSkipped={MarkerCleanupSkipped}; ArtifactFileSetsConfirmed={ArtifactFileSetsConfirmed}; ArtifactFileSetsRolledBack={ArtifactFileSetsRolledBack}; ArtifactFileSetsFailed={ArtifactFileSetsFailed}; ArtifactFileSetsActive={ArtifactFileSetsActive}; ArtifactJournalUnreadable={ArtifactJournalUnreadable}; ReclaimedQuotaReservations={ReclaimedQuotaReservations}",
                    result.ReconciledCommittedFiles,
                    result.ReconciledRolledBackFiles,
                    result.FailedFileReconciliations,
                    result.SkippedActiveFileReconciliations,
                    result.DeletedCommitMarkers,
                    result.MarkerCleanupSkipped,
                    artifactFileSetResult.ConfirmedOperations,
                    artifactFileSetResult.RolledBackOperations,
                    artifactFileSetResult.FailedOperations,
                    artifactFileSetResult.ActiveOperations,
                    artifactFileSetResult.HasUnreadableJournal,
                    reclaimedQuotaReservations);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Persistence maintenance failed and will retry. ErrorType={ErrorType}",
                    exception.GetType().Name);
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
