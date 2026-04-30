namespace AICopilot.SharedKernel.Domain;

public abstract class BaseEntity<TId> : IEntity<TId>, IHasDomainEvents
{
    private readonly List<object> _domainEvents = [];

    public TId Id { get; protected set; } = default!;

    public IReadOnlyCollection<object> DomainEvents => _domainEvents.AsReadOnly();

    protected void AddDomainEvent(object domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }
}
