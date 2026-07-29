using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Transactions;

internal static class ArtifactFinalReviewMutationGuard
{
    public static async Task ValidateAsync(
        AiGatewayDbContext context,
        CancellationToken cancellationToken)
    {
        var mutations = context.ChangeTracker
            .Entries<Artifact>()
            .Where(entry =>
                entry.State == EntityState.Modified &&
                entry.Property(artifact => artifact.Version).CurrentValue !=
                entry.Property(artifact => artifact.Version).OriginalValue)
            .Select(entry => new ArtifactMutation(
                entry.Entity.TaskId.Value,
                entry.Entity.WorkspaceId.Value,
                entry.Entity.Id.Value))
            .Distinct()
            .OrderBy(mutation => mutation.TaskId)
            .ThenBy(mutation => mutation.WorkspaceId)
            .ThenBy(mutation => mutation.ArtifactId)
            .ToArray();
        if (mutations.Length == 0)
        {
            return;
        }

        foreach (var taskGroup in mutations.GroupBy(mutation => mutation.TaskId))
        {
            var taskId = new AgentTaskId(taskGroup.Key);
            var task = await context.AgentTasks
                .FromSqlInterpolated($$"""
                    SELECT task.*, task.xmin
                    FROM aigateway.agent_tasks AS task
                    WHERE id = {{taskGroup.Key}}
                    FOR UPDATE
                    """)
                .AsNoTracking()
                .SingleOrDefaultAsync(cancellationToken);
            if (task is null || task.Status != AgentTaskStatus.WorkspaceReady)
            {
                ThrowConflict(context, taskGroup.Key);
            }

            foreach (var workspaceGroup in taskGroup.GroupBy(mutation => mutation.WorkspaceId))
            {
                var workspace = await context.ArtifactWorkspaces
                    .FromSqlInterpolated($$"""
                        SELECT workspace.*, workspace.xmin
                        FROM aigateway.artifact_workspaces AS workspace
                        WHERE id = {{workspaceGroup.Key}}
                        FOR UPDATE
                        """)
                    .AsNoTracking()
                    .SingleOrDefaultAsync(cancellationToken);
                if (workspace is null ||
                    workspace.TaskId.Value != taskGroup.Key ||
                    workspace.Status == ArtifactWorkspaceStatus.Finalized)
                {
                    ThrowConflict(context, taskGroup.Key);
                }

                var lockedArtifactIds = await context.Set<Artifact>()
                    .FromSqlInterpolated($$"""
                        SELECT artifact.*, artifact.xmin
                        FROM aigateway.artifacts AS artifact
                        WHERE workspace_id = {{workspaceGroup.Key}}
                        ORDER BY id
                        FOR UPDATE
                        """)
                    .AsNoTracking()
                    .Select(artifact => artifact.Id.Value)
                    .ToArrayAsync(cancellationToken);
                if (workspaceGroup.Any(mutation =>
                        !lockedArtifactIds.Contains(mutation.ArtifactId)))
                {
                    ThrowConflict(context, taskGroup.Key);
                }
            }

            var hasFinalOutputApproval =
                context.ChangeTracker
                    .Entries<ApprovalRequest>()
                    .Any(entry =>
                        entry.State == EntityState.Added &&
                        entry.Entity.TaskId.Value == taskGroup.Key &&
                        entry.Entity.ApprovalType == AgentApprovalType.FinalOutput) ||
                await context.ApprovalRequests
                    .AsNoTracking()
                    .AnyAsync(
                        approval =>
                            approval.TaskId == taskId &&
                            approval.ApprovalType == AgentApprovalType.FinalOutput,
                        cancellationToken);
            if (hasFinalOutputApproval)
            {
                ThrowConflict(context, taskGroup.Key);
            }
        }
    }

    private static void ThrowConflict(AiGatewayDbContext context, Guid taskId)
    {
        context.ChangeTracker.Clear();
        throw new ArtifactFinalReviewMutationConflictException(taskId);
    }

    private readonly record struct ArtifactMutation(
        Guid TaskId,
        Guid WorkspaceId,
        Guid ArtifactId);
}
