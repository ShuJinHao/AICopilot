using System.Text.Json;

namespace AICopilot.EntityFrameworkCore.Outbox;

public sealed class OutboxMessage
{
    private const int MaxRetryCount = 5;

    private OutboxMessage()
    {
    }

    public Guid Id { get; private set; }

    public string EventType { get; private set; } = string.Empty;

    public string EventTypeName { get; private set; } = string.Empty;

    public string Payload { get; private set; } = string.Empty;

    public DateTime OccurredOnUtc { get; private set; }

    public DateTime? ProcessedOnUtc { get; private set; }

    public DateTime? DeadLetteredOnUtc { get; private set; }

    public DateTime? NextAttemptUtc { get; private set; }

    public int RetryCount { get; private set; }

    public string? Error { get; private set; }

    public static OutboxMessage FromIntegrationEvent(object integrationEvent)
    {
        var eventType = integrationEvent.GetType();
        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = eventType.AssemblyQualifiedName ?? eventType.FullName ?? eventType.Name,
            EventTypeName = eventType.FullName ?? eventType.Name,
            Payload = JsonSerializer.Serialize(integrationEvent, eventType),
            OccurredOnUtc = DateTime.UtcNow
        };
    }

    public void MarkProcessed(DateTime processedOnUtc)
    {
        ProcessedOnUtc = processedOnUtc;
        Error = null;
        NextAttemptUtc = null;
    }

    public void MarkFailed(string error, DateTime failedOnUtc)
    {
        RetryCount++;
        Error = error.Length > 4000 ? error[..4000] : error;

        if (RetryCount >= MaxRetryCount)
        {
            DeadLetteredOnUtc = failedOnUtc;
            NextAttemptUtc = null;
            return;
        }

        NextAttemptUtc = failedOnUtc.AddSeconds(Math.Min(60, Math.Pow(2, RetryCount)));
    }
}
