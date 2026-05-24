using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Ids;

public readonly record struct PilotAuthorizationSubmissionId : IStronglyTypedGuidId
{
    public PilotAuthorizationSubmissionId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Pilot authorization submission id is required.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static PilotAuthorizationSubmissionId New() => new(Guid.NewGuid());

    public static implicit operator Guid(PilotAuthorizationSubmissionId id) => id.Value;

    public override string ToString() => Value.ToString();
}
