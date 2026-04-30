namespace AICopilot.SharedKernel.Domain;

public interface IHasDomainEvents
{
    IReadOnlyCollection<object> DomainEvents { get; }

    void ClearDomainEvents();
}
