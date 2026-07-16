using AICopilot.Services.Contracts;

namespace AICopilot.AiRuntime;

internal sealed class EndpointRuntimeStats
{
    private readonly List<double> durations = [];
    private readonly Queue<DateTimeOffset> requestWindow = new();
    private readonly Queue<TokenWindowItem> tokenWindow = new();
    private DateTimeOffset? circuitOpenUntil;

    public int InFlight { get; private set; }

    public int QueueLength { get; private set; }

    public int ConsecutiveFailures { get; private set; }

    public long SuccessCount { get; private set; }

    public long FailureCount { get; private set; }

    public long RateLimitCount { get; private set; }

    public long CircuitBreakerOpenCount { get; private set; }

    public int StickyStreamingCount { get; private set; }

    public string? LastFailureReason { get; private set; }

    public bool IsCircuitOpen(DateTimeOffset now)
    {
        return circuitOpenUntil.HasValue && now < circuitOpenUntil.Value;
    }

    public void RecordStarted()
    {
        InFlight++;
    }

    public void RecordStarted(int tokenEstimate, DateTimeOffset now)
    {
        InFlight++;
        requestWindow.Enqueue(now);
        tokenWindow.Enqueue(new TokenWindowItem(now, tokenEstimate));
    }

    public void RecordSucceeded(TimeSpan duration)
    {
        InFlight = Math.Max(0, InFlight - 1);
        SuccessCount++;
        ConsecutiveFailures = 0;
        circuitOpenUntil = null;
        AddDuration(duration);
    }

    public void RecordFailed(TimeSpan duration, Exception exception)
    {
        InFlight = Math.Max(0, InFlight - 1);
        FailureCount++;
        ConsecutiveFailures++;
        LastFailureReason = exception.GetType().Name;
        AddDuration(duration);
    }

    public void OpenCircuit(TimeSpan duration, DateTimeOffset now)
    {
        circuitOpenUntil = now.Add(duration);
        CircuitBreakerOpenCount++;
    }

    public void RecordRateLimited()
    {
        RateLimitCount++;
    }

    public void RecordStickyStreaming()
    {
        StickyStreamingCount++;
    }

    public void IncrementQueue()
    {
        QueueLength++;
    }

    public void DecrementQueue()
    {
        QueueLength = Math.Max(0, QueueLength - 1);
    }

    public bool CanReserve(int tokenEstimate, int rpmLimit, int tpmLimit, DateTimeOffset now)
    {
        PruneWindows(now);
        var withinRpm = rpmLimit <= 0 || requestWindow.Count < rpmLimit;
        var withinTpm = tpmLimit <= 0 || tokenWindow.Sum(item => item.Tokens) + tokenEstimate <= tpmLimit;
        return withinRpm && withinTpm;
    }

    public ModelEndpointStatsDto ToDto(int modelInFlight, DateTimeOffset now)
    {
        PruneWindows(now);
        var snapshot = durations.OrderBy(item => item).ToArray();
        var average = snapshot.Length == 0 ? 0 : snapshot.Average();
        var p95 = snapshot.Length == 0
            ? 0
            : snapshot[(int)Math.Floor((snapshot.Length - 1) * 0.95)];

        return new ModelEndpointStatsDto(
            InFlight,
            QueueLength,
            modelInFlight,
            SuccessCount,
            FailureCount,
            average,
            p95,
            RateLimitCount,
            CircuitBreakerOpenCount,
            StickyStreamingCount,
            IsCircuitOpen(now) ? "Open" : "Closed",
            LastFailureReason);
    }

    private void AddDuration(TimeSpan duration)
    {
        durations.Add(Math.Max(0, duration.TotalMilliseconds));
        if (durations.Count > 512)
        {
            durations.RemoveAt(0);
        }
    }

    private void PruneWindows(DateTimeOffset now)
    {
        var min = now.AddMinutes(-1);
        while (requestWindow.Count > 0 && requestWindow.Peek() < min)
        {
            requestWindow.Dequeue();
        }

        while (tokenWindow.Count > 0 && tokenWindow.Peek().Timestamp < min)
        {
            tokenWindow.Dequeue();
        }
    }

    private sealed record TokenWindowItem(DateTimeOffset Timestamp, int Tokens);
}

internal sealed class ModelRuntimeStats
{
    public int InFlight { get; private set; }

    public void RecordStarted()
    {
        InFlight++;
    }

    public void RecordCompleted()
    {
        InFlight = Math.Max(0, InFlight - 1);
    }
}

internal sealed class QuotaWindow
{
    private readonly Queue<DateTimeOffset> requests = new();

    public bool CanReserve(int limit, DateTimeOffset now)
    {
        Prune(now);
        return requests.Count < limit;
    }

    public void Reserve(DateTimeOffset now)
    {
        Prune(now);
        requests.Enqueue(now);
    }

    private void Prune(DateTimeOffset now)
    {
        var min = now.AddMinutes(-1);
        while (requests.Count > 0 && requests.Peek() < min)
        {
            requests.Dequeue();
        }
    }
}
