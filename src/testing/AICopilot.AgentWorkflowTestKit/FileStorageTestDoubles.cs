using AICopilot.Services.Contracts;

namespace AICopilot.AgentWorkflowTestKit;

internal sealed class CapturingFileStorage(Exception? deleteException = null)
    : IFileStorageService, IPersistenceFileStorageService
{
    private IPersistenceCommitScope? commitScope;

    public int SaveCount { get; private set; }

    public int DeleteCount { get; private set; }

    public int ConfirmCount { get; private set; }

    public int PendingCount { get; private set; }

    public List<string> DeletedPaths { get; } = [];

    public void AttachCommitScope(IPersistenceCommitScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (commitScope is not null && !ReferenceEquals(commitScope, scope))
        {
            throw new InvalidOperationException("A different persistence commit scope is already attached.");
        }

        commitScope = scope;
    }

    public Task<Stream?> GetAsync(string path, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<Stream?>(null);
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        DeleteCount++;
        DeletedPaths.Add(path);
        if (deleteException is not null)
        {
            throw deleteException;
        }

        return Task.CompletedTask;
    }

    public Task<PersistenceFileStage> StageAsync(
        Stream stream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        SaveCount++;
        var commitId = commitScope?.ReserveCommitId() ?? Guid.NewGuid();
        return Task.FromResult(new PersistenceFileStage(commitId, fileName));
    }

    public Task ConfirmBestEffortAsync(
        PersistenceFileStage stage,
        CancellationToken cancellationToken = default)
    {
        ConfirmCount++;
        commitScope?.ReleaseCommitId(stage.CommitId);
        return Task.CompletedTask;
    }

    public async Task RollbackBestEffortAsync(
        PersistenceFileStage stage,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await DeleteAsync(stage.StoragePath, cancellationToken);
        }
        catch when (deleteException is not null)
        {
        }
        finally
        {
            commitScope?.ReleaseCommitId(stage.CommitId);
        }
    }

    public Task LeavePendingAsync(
        PersistenceFileStage stage,
        CancellationToken cancellationToken = default)
    {
        PendingCount++;
        commitScope?.ReleaseCommitId(stage.CommitId);
        return Task.CompletedTask;
    }
}

internal sealed class FixedDocumentFormatPolicy(IReadOnlyCollection<string> supportedExtensions)
    : IDocumentFormatPolicy
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = supportedExtensions;

    public bool IsSupported(string extension)
    {
        return SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }
}
