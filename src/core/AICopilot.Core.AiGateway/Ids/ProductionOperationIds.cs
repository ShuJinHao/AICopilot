using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Ids;

public readonly record struct ProductionPilotEmergencyStopStateId : IStronglyTypedGuidId
{
    public static readonly ProductionPilotEmergencyStopStateId Default = new(new Guid("a7a01597-8f2c-4f54-8a43-35642c09f142"));

    public ProductionPilotEmergencyStopStateId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Production Pilot emergency stop state id is required.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static ProductionPilotEmergencyStopStateId New() => new(Guid.NewGuid());

    public static implicit operator Guid(ProductionPilotEmergencyStopStateId id) => id.Value;

    public override string ToString() => Value.ToString();
}

public readonly record struct ProductionPilotIncidentId : IStronglyTypedGuidId
{
    public ProductionPilotIncidentId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Production Pilot incident id is required.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static ProductionPilotIncidentId New() => new(Guid.NewGuid());

    public static implicit operator Guid(ProductionPilotIncidentId id) => id.Value;

    public override string ToString() => Value.ToString();
}

public readonly record struct ProductionPilotRunLedgerId : IStronglyTypedGuidId
{
    public ProductionPilotRunLedgerId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Production Pilot run ledger id is required.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static ProductionPilotRunLedgerId New() => new(Guid.NewGuid());

    public static implicit operator Guid(ProductionPilotRunLedgerId id) => id.Value;

    public override string ToString() => Value.ToString();
}

public readonly record struct ProductionPilotWindowId : IStronglyTypedGuidId
{
    public ProductionPilotWindowId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Production Pilot window id is required.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static ProductionPilotWindowId New() => new(Guid.NewGuid());

    public static implicit operator Guid(ProductionPilotWindowId id) => id.Value;

    public override string ToString() => Value.ToString();
}

public readonly record struct ProductionPilotRunId : IStronglyTypedGuidId
{
    public ProductionPilotRunId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Production Pilot run id is required.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static ProductionPilotRunId New() => new(Guid.NewGuid());

    public static implicit operator Guid(ProductionPilotRunId id) => id.Value;

    public override string ToString() => Value.ToString();
}

public readonly record struct ProductionControlledPilotIntentId : IStronglyTypedGuidId
{
    public ProductionControlledPilotIntentId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Production controlled Pilot intent id is required.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static ProductionControlledPilotIntentId New() => new(Guid.NewGuid());

    public static implicit operator Guid(ProductionControlledPilotIntentId id) => id.Value;

    public override string ToString() => Value.ToString();
}

public readonly record struct ProductionControlledPilotRunId : IStronglyTypedGuidId
{
    public ProductionControlledPilotRunId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Production controlled Pilot run id is required.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static ProductionControlledPilotRunId New() => new(Guid.NewGuid());

    public static implicit operator Guid(ProductionControlledPilotRunId id) => id.Value;

    public override string ToString() => Value.ToString();
}

public readonly record struct ProductionPilotGaReadinessAssessmentId : IStronglyTypedGuidId
{
    public ProductionPilotGaReadinessAssessmentId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Production Pilot GA readiness assessment id is required.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static ProductionPilotGaReadinessAssessmentId New() => new(Guid.NewGuid());

    public static implicit operator Guid(ProductionPilotGaReadinessAssessmentId id) => id.Value;

    public override string ToString() => Value.ToString();
}
