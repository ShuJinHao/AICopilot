using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.DataAnalysis.Ids;

public readonly record struct BusinessDatabaseId : IStronglyTypedGuidId
{
    public BusinessDatabaseId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Business database id is required.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static BusinessDatabaseId New() => new(Guid.NewGuid());

    public static implicit operator Guid(BusinessDatabaseId id) => id.Value;

    public override string ToString() => Value.ToString();
}
