using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.Artifacts;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Workspaces;

public sealed class AgentArtifactWorkspaceService(
    IRepository<ArtifactWorkspace> workspaceRepository,
    IArtifactWorkspaceFileStore fileStore,
    IArtifactWorkspaceFileSetStore fileSetStore,
    IArtifactFileSetOperationStore operationStore,
    AgentRuntimeWriteAuthorityAccessor writeAuthorityAccessor)
    : IAgentArtifactWorkspaceService
{
    public async Task<ArtifactWorkspace> CreateForTaskAsync(
        AgentTask task,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (task.WorkspaceId is not null)
        {
            var existing = await workspaceRepository.FirstOrDefaultAsync(
                new ArtifactWorkspaceByIdSpec(task.WorkspaceId.Value, includeArtifacts: true),
                cancellationToken);
            if (existing is not null)
            {
                return existing;
            }
        }

        var workspaceCode = $"ws_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}"[..38];
        var storage = await fileStore.CreateWorkspaceAsync(workspaceCode, task.Id.Value, cancellationToken);
        var workspace = new ArtifactWorkspace(
            task.Id,
            workspaceCode,
            storage.RootPath,
            storage.WorkspaceUrl,
            nowUtc);
        workspaceRepository.Add(workspace);
        return workspace;
    }

    public async Task<Artifact> WriteDraftTextArtifactAsync(
        ArtifactWorkspace workspace,
        ArtifactType artifactType,
        string name,
        string relativePath,
        string content,
        string mimeType,
        AgentStepId? stepId,
        ArtifactSourceMetadata? sourceMetadata,
        CancellationToken cancellationToken)
    {
        EnsureCanWriteDraftArtifact(workspace);
        return await WriteDraftArtifactAsync(
            workspace,
            artifactType,
            name,
            relativePath,
            System.Text.Encoding.UTF8.GetBytes(content),
            mimeType,
            stepId,
            sourceMetadata,
            cancellationToken);
    }

    public async Task<Artifact> WriteDraftBinaryArtifactAsync(
        ArtifactWorkspace workspace,
        ArtifactType artifactType,
        string name,
        string relativePath,
        byte[] content,
        string mimeType,
        AgentStepId? stepId,
        ArtifactSourceMetadata? sourceMetadata,
        CancellationToken cancellationToken)
    {
        EnsureCanWriteDraftArtifact(workspace);
        return await WriteDraftArtifactAsync(
            workspace,
            artifactType,
            name,
            relativePath,
            content,
            mimeType,
            stepId,
            sourceMetadata,
            cancellationToken);
    }

    private async Task<Artifact> WriteDraftArtifactAsync(
        ArtifactWorkspace workspace,
        ArtifactType artifactType,
        string name,
        string relativePath,
        byte[] content,
        string mimeType,
        AgentStepId? stepId,
        ArtifactSourceMetadata? sourceMetadata,
        CancellationToken cancellationToken)
    {
        var artifacts = await WriteDraftArtifactSetAsync(
            workspace,
            [new AgentDraftArtifactWriteRequest(
                artifactType,
                name,
                relativePath,
                content,
                mimeType,
                stepId,
                sourceMetadata)],
            cancellationToken);
        return artifacts.Single();
    }

    public async Task<IReadOnlyList<Artifact>> WriteDraftArtifactSetAsync(
        ArtifactWorkspace workspace,
        IReadOnlyCollection<AgentDraftArtifactWriteRequest> artifacts,
        CancellationToken cancellationToken)
    {
        EnsureCanWriteDraftArtifact(workspace);
        if (artifacts.Count == 0 || artifacts.Count > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(artifacts), "Draft artifact set must contain between 1 and 256 files.");
        }

        var prepared = artifacts.Select(request =>
        {
            ArgumentNullException.ThrowIfNull(request.Content);
            var normalizedPath = ArtifactPathGuard.NormalizeDraftPath(request.RelativePath);
            var separator = normalizedPath.IndexOf('/');
            if (separator <= 0 || separator == normalizedPath.Length - 1)
            {
                throw new InvalidOperationException("Draft artifact path must include a controlled workspace folder and file name.");
            }

            return new
            {
                Request = request,
                RootFolder = normalizedPath[..separator],
                FileSetRelativePath = normalizedPath[(separator + 1)..]
            };
        }).ToArray();
        var roots = prepared.Select(item => item.RootFolder).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (roots.Length != 1)
        {
            throw new InvalidOperationException("One atomic draft artifact set cannot span multiple workspace root folders.");
        }

        if (prepared.Select(item => item.FileSetRelativePath)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != prepared.Length)
        {
            throw new InvalidOperationException("Draft artifact set contains duplicate target paths.");
        }

        var writeAuthority = writeAuthorityAccessor.Current;
        var stage = await fileSetStore.StageAsync(
            workspace.WorkspaceCode,
            prepared.Length == 1 ? "CreateDraftArtifact" : "CreateDraftArtifactSet",
            $"{roots[0]}/.committed",
            prepared.Select(item => new ArtifactFileSetWriteRequest(
                item.FileSetRelativePath,
                item.Request.Content,
                item.Request.MimeType)).ToArray(),
            cancellationToken,
            new ArtifactFileSetAuthority(
                workspace.TaskId.Value,
                writeAuthority?.NodeRunId.Value,
                writeAuthority?.TaskFencingToken ?? 0,
                writeAuthority?.NodeFencingToken ?? 0));
        return await fileSetStore.ExecuteAsync(
            stage,
            async commitCancellationToken =>
            {
                var createdArtifacts = new List<Artifact>(prepared.Length);
                ArtifactFileSetOperation? operation = null;
                try
                {
                    var now = DateTimeOffset.UtcNow;
                    foreach (var item in prepared)
                    {
                        var publishedPath = $"{stage.PublishedReference}/{item.FileSetRelativePath}";
                        var published = stage.Files.Single(file =>
                            string.Equals(file.RelativePath, publishedPath, StringComparison.Ordinal));
                        var artifact = workspace.AddDraftArtifact(
                            item.Request.ArtifactType,
                            item.Request.Name,
                            published.RelativePath,
                            published.FileSize,
                            published.MimeType,
                            item.Request.StepId,
                            now);
                        artifact.ApplySourceMetadata(item.Request.SourceMetadata);
                        createdArtifacts.Add(artifact);
                    }

                    operation = new ArtifactFileSetOperation(
                        stage.CommitId,
                        workspace.TaskId,
                        workspace.Id,
                        writeAuthority?.NodeRunId,
                        writeAuthority?.TaskFencingToken ?? 0,
                        writeAuthority?.NodeFencingToken ?? 0,
                        stage.OperationKind,
                        stage.ManifestJson,
                        stage.ManifestDigest,
                        stage.StagingReference,
                        now);
                    operation.MarkPublished(stage.PublishedReference, stage.ManifestDigest, now);
                    operation.MarkDatabaseCommitted(now);
                    operation.Complete(now);
                    operationStore.AddCompleted(operation);
                    workspaceRepository.Update(workspace);
                    await workspaceRepository.SaveChangesAsync(commitCancellationToken);
                    return (IReadOnlyList<Artifact>)createdArtifacts;
                }
                catch (PersistenceCommitOutcomeUnknownException)
                {
                    throw;
                }
                catch
                {
                    if (operation is not null)
                    {
                        operationStore.Discard(operation);
                    }

                    foreach (var artifact in createdArtifacts)
                    {
                        workspace.RemoveUncommittedDraftArtifact(artifact.Id, DateTimeOffset.UtcNow);
                    }

                    throw;
                }
            },
            cancellationToken);
    }

    private static void EnsureCanWriteDraftArtifact(ArtifactWorkspace workspace)
    {
        if (workspace.Status == ArtifactWorkspaceStatus.Finalized)
        {
            throw new InvalidOperationException($"{AppProblemCodes.ArtifactFinalized}: Finalized workspaces cannot receive draft artifacts.");
        }
    }
}
