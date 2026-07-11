namespace AICopilot.EntityFrameworkCore.Persistence;

public sealed class PersistenceCommitMarker
{
    private PersistenceCommitMarker()
    {
    }

    public PersistenceCommitMarker(Guid id, string operationName, DateTime createdAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Persistence commit id is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(operationName))
        {
            throw new ArgumentException("Persistence operation name is required.", nameof(operationName));
        }

        Id = id;
        OperationName = operationName.Trim();
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public string OperationName { get; private set; } = string.Empty;

    public DateTime CreatedAtUtc { get; private set; }
}
