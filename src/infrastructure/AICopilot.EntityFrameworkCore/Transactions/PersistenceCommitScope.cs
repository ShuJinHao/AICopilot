using AICopilot.Services.Contracts;

namespace AICopilot.EntityFrameworkCore.Transactions;

public sealed class PersistenceCommitScope : IPersistenceCommitScope
{
    private readonly object syncRoot = new();
    private Guid? currentCommitId;

    public Guid? CurrentCommitId
    {
        get
        {
            lock (syncRoot)
            {
                return currentCommitId;
            }
        }
    }

    public Guid ReserveCommitId()
    {
        lock (syncRoot)
        {
            if (currentCommitId.HasValue)
            {
                throw new InvalidOperationException(
                    "A persistence commit id is already reserved in the current service scope.");
            }

            currentCommitId = Guid.NewGuid();
            return currentCommitId.Value;
        }
    }

    public void ReleaseCommitId(Guid commitId)
    {
        if (commitId == Guid.Empty)
        {
            throw new ArgumentException("Persistence commit id is required.", nameof(commitId));
        }

        lock (syncRoot)
        {
            if (!currentCommitId.HasValue)
            {
                return;
            }

            if (currentCommitId.Value != commitId)
            {
                throw new InvalidOperationException(
                    "The reserved persistence commit id does not match the requested release.");
            }

            currentCommitId = null;
        }
    }
}
