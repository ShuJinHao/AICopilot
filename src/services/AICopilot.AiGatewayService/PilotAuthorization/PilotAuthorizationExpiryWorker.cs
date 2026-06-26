using AICopilot.Core.AiGateway.Aggregates.PilotAuthorization;
using AICopilot.Core.AiGateway.Specifications.PilotAuthorization;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AICopilot.AiGatewayService.PilotAuthorization;

public sealed class PilotAuthorizationExpiryWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<PilotAuthorizationExpiryWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessOnceAsync(stoppingToken);
                if (!processed)
                {
                    await Task.Delay(PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Pilot authorization expiry worker iteration failed.");
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    public async Task<bool> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<PilotAuthorizationSubmission>>();
        var auditLogWriter = scope.ServiceProvider.GetRequiredService<IAuditLogWriter>();
        return await ExpireDueSubmissionsAsync(
            repository,
            auditLogWriter,
            DateTimeOffset.UtcNow,
            cancellationToken);
    }

    internal static async Task<bool> ExpireDueSubmissionsAsync(
        IRepository<PilotAuthorizationSubmission> repository,
        IAuditLogWriter auditLogWriter,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var dueSubmissions = await repository.ListAsync(
            new PilotAuthorizationExpiredOpenSubmissionsSpec(nowUtc),
            cancellationToken);

        foreach (var submission in dueSubmissions)
        {
            submission.ExpireBySystem(nowUtc);
            repository.Update(submission);
            await PilotAuthorizationAudit.WriteAsync(
                auditLogWriter,
                PilotAuthorizationAuditActions.Expired,
                AuditResults.Succeeded,
                submission,
                "Expired Pilot authorization package by DataWorker before any execution permission.",
                cancellationToken);
        }

        if (dueSubmissions.Count == 0)
        {
            return false;
        }

        await repository.SaveChangesAsync(cancellationToken);
        return true;
    }
}
