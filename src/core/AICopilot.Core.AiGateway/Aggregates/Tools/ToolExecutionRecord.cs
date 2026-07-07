using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.Tools;

public enum ToolExecutionStatus
{
    Running = 0,
    Succeeded = 1,
    Failed = 2,
    Rejected = 3
}

public sealed class ToolExecutionRecord : BaseEntity<ToolExecutionRecordId>
{
    private ToolExecutionRecord()
    {
    }

    public ToolExecutionRecord(
        AgentTaskId taskId,
        AgentStepId stepId,
        string toolCode,
        string? inputSummary,
        DateTimeOffset startedAt,
        AgentTaskRunAttemptId? runAttemptId = null)
    {
        Id = ToolExecutionRecordId.New();
        TaskId = taskId;
        StepId = stepId;
        RunAttemptId = runAttemptId;
        ToolCode = NormalizeRequired(toolCode, nameof(toolCode), 160);
        InputSummary = NormalizeOptional(inputSummary, 2000);
        Status = ToolExecutionStatus.Running;
        StartedAt = startedAt;
    }

    public AgentTaskId TaskId { get; private set; }

    public AgentStepId StepId { get; private set; }

    public AgentTaskRunAttemptId? RunAttemptId { get; private set; }

    public string ToolCode { get; private set; } = string.Empty;

    public string? InputSummary { get; private set; }

    public string? OutputSummary { get; private set; }

    public ToolExecutionStatus Status { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public long? DurationMs { get; private set; }

    public string? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    public string? ArtifactId { get; private set; }

    public string? AuditMetadata { get; private set; }

    public void MarkSucceeded(
        string? outputSummary,
        string? artifactId,
        string? auditMetadata,
        DateTimeOffset completedAt)
    {
        Complete(ToolExecutionStatus.Succeeded, outputSummary, null, null, artifactId, auditMetadata, completedAt);
    }

    public void MarkFailed(
        string errorCode,
        string errorMessage,
        string? auditMetadata,
        DateTimeOffset completedAt)
    {
        Complete(ToolExecutionStatus.Failed, null, errorCode, errorMessage, null, auditMetadata, completedAt);
    }

    public void MarkRejected(
        string errorCode,
        string errorMessage,
        string? auditMetadata,
        DateTimeOffset completedAt)
    {
        Complete(ToolExecutionStatus.Rejected, null, errorCode, errorMessage, null, auditMetadata, completedAt);
    }

    private void Complete(
        ToolExecutionStatus status,
        string? outputSummary,
        string? errorCode,
        string? errorMessage,
        string? artifactId,
        string? auditMetadata,
        DateTimeOffset completedAt)
    {
        if (Status != ToolExecutionStatus.Running)
        {
            throw new InvalidOperationException("Only running tool execution records can be completed.");
        }

        Status = status;
        OutputSummary = NormalizeOptional(outputSummary, 4000);
        ErrorCode = NormalizeOptional(errorCode, 120);
        ErrorMessage = NormalizeOptional(errorMessage, 2000);
        ArtifactId = NormalizeOptional(artifactId, 80);
        AuditMetadata = NormalizeOptional(auditMetadata, 4000);
        CompletedAt = completedAt;
        DurationMs = Math.Max(0, (long)(completedAt - StartedAt).TotalMilliseconds);
    }

    private static string NormalizeRequired(string value, string paramName, int maxLength)
    {
        var normalized = NormalizeOptional(value, maxLength);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException($"{paramName} is required.", paramName);
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is { Length: > 0 } && normalized.Length > maxLength
            ? normalized[..maxLength]
            : normalized;
    }
}
