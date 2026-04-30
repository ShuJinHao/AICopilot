namespace AICopilot.SharedKernel.Domain;

public interface IStronglyTypedId<out TValue>
{
    TValue Value { get; }
}

public interface IStronglyTypedGuidId : IStronglyTypedId<Guid>;

public interface IStronglyTypedIntId : IStronglyTypedId<int>;
