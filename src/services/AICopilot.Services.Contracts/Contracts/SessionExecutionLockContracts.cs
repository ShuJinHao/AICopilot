namespace AICopilot.Services.Contracts;

public interface ISessionExecutionLock
{
    ValueTask<IAsyncDisposable> AcquireAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
