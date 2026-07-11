using AICopilot.Core.Rag.Ids;
using AICopilot.Services.Contracts;

namespace AICopilot.BackendTests;

internal sealed class SequentialDocumentIdAllocator(int initialValue = 1) : IDocumentIdAllocator
{
    private int currentValue = initialValue - 1;

    public Task<DocumentId> AllocateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new DocumentId(Interlocked.Increment(ref currentValue)));
    }
}
