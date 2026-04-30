using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AICopilot.EntityFrameworkCore.Outbox;

public sealed class OutboxDispatcher(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private static readonly TimeSpan PollingDelay = TimeSpan.FromSeconds(2);
    private const int BatchSize = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await DispatchOnceAsync(stoppingToken);
                await Task.Delay(PollingDelay, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown path.
        }
    }

    private async Task DispatchOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var now = DateTime.UtcNow;
        var strategy = dbContext.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var messages = await dbContext.OutboxMessages
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM outbox.outbox_messages
                    WHERE processed_on_utc IS NULL
                      AND dead_lettered_on_utc IS NULL
                      AND (next_attempt_utc IS NULL OR next_attempt_utc <= {now})
                    ORDER BY occurred_on_utc
                    LIMIT {BatchSize}
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(cancellationToken);

            foreach (var message in messages)
            {
                try
                {
                    var eventType = Type.GetType(message.EventType, throwOnError: false)
                                    ?? throw new InvalidOperationException($"Cannot resolve outbox event type '{message.EventType}'.");
                    var integrationEvent = JsonSerializer.Deserialize(message.Payload, eventType)
                                          ?? throw new InvalidOperationException($"Cannot deserialize outbox message {message.Id}.");

                    await publishEndpoint.Publish(integrationEvent, cancellationToken);
                    message.MarkProcessed(DateTime.UtcNow);
                }
                catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogInformation(ex, "Publishing outbox message {OutboxMessageId} was cancelled; it will be retried without incrementing retry count.", message.Id);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to publish outbox message {OutboxMessageId}.", message.Id);
                    message.MarkFailed(ex.Message, DateTime.UtcNow);
                }
            }

            if (messages.Count > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        });
    }
}
