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
