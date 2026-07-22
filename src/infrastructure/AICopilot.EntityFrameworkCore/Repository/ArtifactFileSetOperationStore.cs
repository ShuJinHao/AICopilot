using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Repository;

internal sealed class ArtifactFileSetOperationStore(AiGatewayDbContext dbContext)
    : IArtifactFileSetOperationStore
{
    public void AddCompleted(ArtifactFileSetOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.Status != ArtifactFileSetOperationStatus.Completed)
        {
            throw new InvalidOperationException("Only a completed file-set operation can enter its database checkpoint.");
        }

        dbContext.ArtifactFileSetOperations.Add(operation);
    }

    public void Discard(ArtifactFileSetOperation operation)
    {
        var entry = dbContext.Entry(operation);
        if (entry.State == Microsoft.EntityFrameworkCore.EntityState.Added)
        {
            entry.State = Microsoft.EntityFrameworkCore.EntityState.Detached;
        }
    }

    public async Task<IReadOnlyCollection<ArtifactFileSetOutcomeAuthoritySnapshot>> ListByNodeFenceAsync(
        AgentNodeRunId nodeRunId,
        long taskFencingToken,
        long nodeFencingToken,
        CancellationToken cancellationToken = default)
    {
        var operations = await dbContext.ArtifactFileSetOperations
            .AsNoTracking()
            .Where(operation =>
                operation.NodeRunId == nodeRunId &&
                operation.TaskFencingToken == taskFencingToken &&
                operation.NodeFencingToken == nodeFencingToken)
            .OrderBy(operation => operation.CreatedAtUtc)
            .ToArrayAsync(cancellationToken);
        if (operations.Length == 0)
        {
            return [];
        }

        var task = await dbContext.AgentTasks
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == operations[0].TaskId, cancellationToken);
        var workspace = await dbContext.ArtifactWorkspaces
            .AsNoTracking()
            .Include(candidate => candidate.Artifacts)
            .SingleAsync(candidate => candidate.Id == operations[0].WorkspaceId, cancellationToken);
        return operations
            .Select(operation => new ArtifactFileSetOutcomeAuthoritySnapshot(operation, task, workspace))
            .ToArray();
    }

    public async Task<ArtifactFileSetOutcomeAuthoritySnapshot?> GetByCommitAsync(
        Guid commitId,
        CancellationToken cancellationToken = default)
    {
        var operation = await dbContext.ArtifactFileSetOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.CommitId == commitId, cancellationToken);
        if (operation is null)
        {
            return null;
        }

        var task = await dbContext.AgentTasks
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == operation.TaskId, cancellationToken);
        var workspace = await dbContext.ArtifactWorkspaces
            .AsNoTracking()
            .Include(candidate => candidate.Artifacts)
            .SingleAsync(candidate => candidate.Id == operation.WorkspaceId, cancellationToken);
        return new ArtifactFileSetOutcomeAuthoritySnapshot(operation, task, workspace);
    }
}
