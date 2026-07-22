namespace AICopilot.AiGatewayService.AgentTasks;

public enum AgentRunQueueStaleLeaseAction
{
    Recover = 0,
    Fail = 1
}

public sealed class AgentRunQueueOptions
{
    public const string SectionName = "AgentRunQueue";

    public int LeaseDurationSeconds { get; set; } = 300;

    public int HeartbeatActiveWindowSeconds { get; set; } = 30;

    public int MaxRetryAttempts { get; set; } = 3;

    public int RetryBackoffSeconds { get; set; } = 30;

    public int MaxRetryBackoffSeconds { get; set; } = 300;

    public AgentRunQueueStaleLeaseAction StaleLeaseAction { get; set; } = AgentRunQueueStaleLeaseAction.Recover;

    public TimeSpan LeaseDuration => SecondsOrDefault(LeaseDurationSeconds, 300);

    public TimeSpan HeartbeatActiveWindow => SecondsOrDefault(HeartbeatActiveWindowSeconds, 30);

    public int EffectiveMaxRetryAttempts => Math.Max(0, MaxRetryAttempts);

    public TimeSpan GetRetryBackoff(int retryAttemptNo)
    {
        var normalizedAttempt = Math.Max(1, retryAttemptNo);
        var baseSeconds = Math.Max(1, RetryBackoffSeconds);
        var maxSeconds = Math.Max(baseSeconds, MaxRetryBackoffSeconds);
        var multiplier = Math.Pow(2, normalizedAttempt - 1);
        var seconds = Math.Min(maxSeconds, baseSeconds * multiplier);
        return TimeSpan.FromSeconds(seconds);
    }

    private static TimeSpan SecondsOrDefault(int seconds, int defaultSeconds)
    {
        return TimeSpan.FromSeconds(seconds > 0 ? seconds : defaultSeconds);
    }
}
