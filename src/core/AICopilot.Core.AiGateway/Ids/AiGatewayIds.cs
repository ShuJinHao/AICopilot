using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Ids;

public readonly record struct ApprovalPolicyId : IStronglyTypedGuidId
{
    public ApprovalPolicyId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Approval policy id is required.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static ApprovalPolicyId New() => new(Guid.NewGuid());

    public static implicit operator Guid(ApprovalPolicyId id) => id.Value;

    public override string ToString() => Value.ToString();
}

public readonly record struct ConversationTemplateId : IStronglyTypedGuidId
{
    public ConversationTemplateId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Conversation template id is required.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static ConversationTemplateId New() => new(Guid.NewGuid());

    public static implicit operator Guid(ConversationTemplateId id) => id.Value;

    public override string ToString() => Value.ToString();
}

public readonly record struct LanguageModelId : IStronglyTypedGuidId
{
    public LanguageModelId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Language model id is required.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static LanguageModelId New() => new(Guid.NewGuid());

    public static implicit operator Guid(LanguageModelId id) => id.Value;

    public override string ToString() => Value.ToString();
}

public readonly record struct SessionId : IStronglyTypedGuidId
{
    public SessionId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Session id is required.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static SessionId New() => new(Guid.NewGuid());

    public static implicit operator Guid(SessionId id) => id.Value;

    public override string ToString() => Value.ToString();
}
