using System.Collections.Concurrent;
using System.Threading;

namespace AICopilot.AiGatewayService.Agents;

public interface ISessionExecutionLock
{
    ValueTask<IAsyncDisposable> AcquireAsync(Guid sessionId, CancellationToken cancellationToken = default);
}

public sealed class InMemorySessionExecutionLock : ISessionExecutionLock
{
    private readonly ConcurrentDictionary<Guid, RefCountedSemaphore> _locks = new();

    public async ValueTask<IAsyncDisposable> AcquireAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var holder = _locks.AddOrUpdate(
            sessionId,
            static _ => new RefCountedSemaphore(),
            static (_, existing) =>
            {
                existing.AddRef();
                return existing;
            });

        try
        {
            await holder.Semaphore.WaitAsync(cancellationToken);
            return new Releaser(sessionId, holder, this);
        }
        catch
        {
            ReleaseReference(sessionId, holder);
            throw;
        }
    }

    private void ReleaseReference(Guid sessionId, RefCountedSemaphore holder)
    {
        if (holder.ReleaseRef() != 0)
        {
            return;
        }

        _locks.TryRemove(new KeyValuePair<Guid, RefCountedSemaphore>(sessionId, holder));
        holder.Semaphore.Dispose();
    }

    private sealed class RefCountedSemaphore
    {
        private int _refCount = 1;

        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public void AddRef()
        {
            Interlocked.Increment(ref _refCount);
        }

        public int ReleaseRef()
        {
            return Interlocked.Decrement(ref _refCount);
        }
    }

    private sealed class Releaser(
        Guid sessionId,
        RefCountedSemaphore holder,
        InMemorySessionExecutionLock owner) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
            {
                return ValueTask.CompletedTask;
            }

            holder.Semaphore.Release();
            owner.ReleaseReference(sessionId, holder);
            return ValueTask.CompletedTask;
        }
    }
}
