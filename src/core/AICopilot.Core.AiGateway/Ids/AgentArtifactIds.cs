using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Ids;

public readonly record struct AgentTaskId : IStronglyTypedGuidId
{
    public AgentTaskId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Agent task id is required.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static AgentTaskId New() => new(Guid.NewGuid());

    public static implicit operator Guid(AgentTaskId id) => id.Value;

    public override string ToString() => Value.ToString();
}

public readonly record struct AgentStepId : IStronglyTypedGuidId
{
    public AgentStepId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Agent step id is required.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static AgentStepId New() => new(Guid.NewGuid());

    public static implicit operator Guid(AgentStepId id) => id.Value;

    public override string ToString() => Value.ToString();
}

public readonly record struct ArtifactWorkspaceId : IStronglyTypedGuidId
{
    public ArtifactWorkspaceId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Artifact workspace id is required.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static ArtifactWorkspaceId New() => new(Guid.NewGuid());

    public static implicit operator Guid(ArtifactWorkspaceId id) => id.Value;

    public override string ToString() => Value.ToString();
}

public readonly record struct ArtifactId : IStronglyTypedGuidId
{
    public ArtifactId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Artifact id is required.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static ArtifactId New() => new(Guid.NewGuid());

    public static implicit operator Guid(ArtifactId id) => id.Value;

    public override string ToString() => Value.ToString();
}

public readonly record struct ApprovalRequestId : IStronglyTypedGuidId
{
    public ApprovalRequestId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Approval request id is required.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static ApprovalRequestId New() => new(Guid.NewGuid());

    public static implicit operator Guid(ApprovalRequestId id) => id.Value;

    public override string ToString() => Value.ToString();
}

public readonly record struct ChatRuntimeSettingsId : IStronglyTypedGuidId
{
    public ChatRuntimeSettingsId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Chat runtime settings id is required.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static ChatRuntimeSettingsId New() => new(Guid.NewGuid());

    public static implicit operator Guid(ChatRuntimeSettingsId id) => id.Value;

    public override string ToString() => Value.ToString();
}

public readonly record struct UploadRecordId : IStronglyTypedGuidId
{
    public UploadRecordId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Upload record id is required.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static UploadRecordId New() => new(Guid.NewGuid());

    public static implicit operator Guid(UploadRecordId id) => id.Value;

    public override string ToString() => Value.ToString();
}

public readonly record struct ToolRegistrationId : IStronglyTypedGuidId
{
    public ToolRegistrationId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Tool registration id is required.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static ToolRegistrationId New() => new(Guid.NewGuid());

    public static implicit operator Guid(ToolRegistrationId id) => id.Value;

    public override string ToString() => Value.ToString();
}

public readonly record struct SkillDefinitionId : IStronglyTypedGuidId
{
    public SkillDefinitionId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Skill definition id is required.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static SkillDefinitionId New() => new(Guid.NewGuid());

    public static implicit operator Guid(SkillDefinitionId id) => id.Value;

    public override string ToString() => Value.ToString();
}

public readonly record struct ToolExecutionRecordId : IStronglyTypedGuidId
{
    public ToolExecutionRecordId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Tool execution record id is required.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static ToolExecutionRecordId New() => new(Guid.NewGuid());

    public static implicit operator Guid(ToolExecutionRecordId id) => id.Value;

    public override string ToString() => Value.ToString();
}

public readonly record struct AgentTaskRunAttemptId : IStronglyTypedGuidId
{
    public AgentTaskRunAttemptId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Agent task run attempt id is required.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static AgentTaskRunAttemptId New() => new(Guid.NewGuid());

    public static implicit operator Guid(AgentTaskRunAttemptId id) => id.Value;

    public override string ToString() => Value.ToString();
}

public readonly record struct AgentTaskRunQueueItemId : IStronglyTypedGuidId
{
    public AgentTaskRunQueueItemId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Agent task run queue item id is required.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static AgentTaskRunQueueItemId New() => new(Guid.NewGuid());

    public static implicit operator Guid(AgentTaskRunQueueItemId id) => id.Value;

    public override string ToString() => Value.ToString();
}

public readonly record struct AgentWorkerHeartbeatId : IStronglyTypedGuidId
{
    public AgentWorkerHeartbeatId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Agent worker heartbeat id is required.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static AgentWorkerHeartbeatId New() => new(Guid.NewGuid());

    public static implicit operator Guid(AgentWorkerHeartbeatId id) => id.Value;

    public override string ToString() => Value.ToString();
}

public readonly record struct TrialScenarioRunId : IStronglyTypedGuidId
{
    public TrialScenarioRunId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Trial scenario run id is required.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static TrialScenarioRunId New() => new(Guid.NewGuid());

    public static implicit operator Guid(TrialScenarioRunId id) => id.Value;

    public override string ToString() => Value.ToString();
}

public readonly record struct TrialRiskIssueId : IStronglyTypedGuidId
{
    public TrialRiskIssueId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Trial risk issue id is required.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static TrialRiskIssueId New() => new(Guid.NewGuid());

    public static implicit operator Guid(TrialRiskIssueId id) => id.Value;

    public override string ToString() => Value.ToString();
}
