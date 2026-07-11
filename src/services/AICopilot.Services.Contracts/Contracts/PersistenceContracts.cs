using AICopilot.Core.Rag.Ids;

namespace AICopilot.Services.Contracts;

public interface IDocumentIdAllocator
{
    Task<DocumentId> AllocateAsync(CancellationToken cancellationToken = default);
}

public sealed class PersistenceCommitOutcomeUnknownException : Exception
{
    public PersistenceCommitOutcomeUnknownException(Guid commitId, Exception innerException)
        : base(
            $"Persistence commit outcome could not be verified. CommitId={commitId}.",
            innerException)
    {
        CommitId = commitId;
    }

    public Guid CommitId { get; }
}
