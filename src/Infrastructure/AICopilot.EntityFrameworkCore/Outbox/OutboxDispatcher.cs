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
        while (!stoppingToken.IsCancellationRequested)
        {
            await DispatchOnceAsync(stoppingToken);
            await Task.Delay(PollingDelay, stoppingToken);
        }
    }

    private async Task DispatchOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var now = DateTime.UtcNow;
        var messages = await dbContext.OutboxMessages
            .Where(message => message.ProcessedOnUtc == null
                              && message.DeadLetteredOnUtc == null
                              && (message.NextAttemptUtc == null || message.NextAttemptUtc <= now))
            .OrderBy(message => message.OccurredOnUtc)
            .Take(BatchSize)
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
    }
}
