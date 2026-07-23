using System.Text;
using AICopilot.AiGatewayService.Workspaces;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Services.Contracts;

namespace AICopilot.ApplicationTests;

public sealed class EnterpriseArtifactWorkspaceP9Tests
{
    [Fact]
    public async Task WorkspaceMapperAndPreview_ShouldExposeSimulationArtifactGovernanceMetadata()
    {
        var now = DateTimeOffset.UtcNow;
        var task = CreateWorkspaceReadyTask(now);
        var workspace = CreateWorkspace(task, "ws_p9_sim", now);
        var artifact = workspace.AddDraftArtifact(
            ArtifactType.Markdown,
            "report.md",
            "draft/report.md",
            0,
            "text/markdown",
            task.Steps.Single().Id,
            now);
        artifact.ApplySourceMetadata(new ArtifactSourceMetadata(
            SourceMode: "SimulationBusiness",
            Boundary: "SimulationBusiness",
            IsSimulation: true,
            IsSandbox: false,
            SourceLabel: "AI 独立模拟业务库",
            QueryHash: "sim-query-hash-001",
            ResultHash: "sim-result-hash-001",
            RowCount: 12,
            IsTruncated: false,
            EvidenceSetDigest: new string('a', 64)));
        artifact.EvidenceSetDigest.Should().Be(new string('a', 64));

        var store = new InMemoryArtifactWorkspaceFileStore();
        await store.WriteTextAsync(workspace.WorkspaceCode, artifact.RelativePath, "# P9 draft\ncapacity summary", artifact.MimeType);
        artifact.AddVersion(artifact.RelativePath, 26, now.AddMinutes(1));

        var files = await store.ListAsync(workspace.WorkspaceCode);
        var workspaceDto = ArtifactWorkspaceMapper.Map(workspace, task, files);
        var draft = workspaceDto.DraftArtifacts.Should().ContainSingle().Which;
        workspaceDto.FinalArtifacts.Should().BeEmpty();
        draft.ArtifactVersion.Should().Be(2);
        draft.ArtifactStatus.Should().Be("Draft");
        draft.SourceMode.Should().Be("SimulationBusiness");
        draft.IsSimulation.Should().BeTrue();
        draft.SourceLabel.Should().Be("AI 独立模拟业务库");
        draft.QueryHash.Should().Be("sim-query-hash-001");
        draft.ResultHash.Should().Be("sim-result-hash-001");
        draft.RowCount.Should().Be(12);
        draft.IsTruncated.Should().BeFalse();

        var preview = await ArtifactPreviewBuilder.BuildAsync(store, workspace.WorkspaceCode, artifact, CancellationToken.None);
        preview.IsSuccess.Should().BeTrue();
        preview.Value!.PreviewKind.Should().Be("markdown");
        preview.Value.ArtifactStatus.Should().Be("Draft");
        preview.Value.ArtifactVersion.Should().Be(2);
        preview.Value.SourceMode.Should().Be("SimulationBusiness");
        preview.Value.IsSimulation.Should().BeTrue();
        preview.Value.QueryHash.Should().Be("sim-query-hash-001");
        preview.Value.Content.Should().Contain("capacity summary");
        preview.Value.Metadata.Should().ContainKey("downloadUrl");
        preview.Value.Metadata["artifactStatus"].Should().Be("Draft");
    }

    [Fact]
    public async Task P9Policy_ShouldRejectMutationAfterFinalReviewOrFinalLock_AndPreserveSandboxMetadata()
    {
        var now = DateTimeOffset.UtcNow;
        var task = CreateWorkspaceReadyTask(now);
        var workspace = CreateWorkspace(task, "ws_p9_sandbox", now);
        var artifact = workspace.AddDraftArtifact(
            ArtifactType.Html,
            "report.html",
            "draft/report.html",
            0,
            "text/html",
            task.Steps.Single().Id,
            now);
        artifact.ApplySourceMetadata(new ArtifactSourceMetadata(
            SourceMode: "CloudReadonlySandbox",
            Boundary: "SandboxControlledTrial",
            IsSimulation: false,
            IsSandbox: true,
            SourceLabel: "Cloud 只读 Sandbox（非生产）",
            QueryHash: "sandbox-query-hash-001",
            ResultHash: "sandbox-result-hash-001",
            RowCount: 8,
            IsTruncated: true));

        var context = new ArtifactVersioningContext(
            workspace,
            task,
            artifact,
            new CurrentUserAccess(task.UserId, "owner", "User", ["AiGateway.EditArtifact"]),
            IsOwner: true);
        var pendingFinalApproval = new ApprovalRequest(
            task.Id,
            AgentApprovalType.FinalOutput,
            workspace.WorkspaceCode,
            task.UserId,
            now);
        var lockedByApproval = await ArtifactWorkspaceP9Policy.ValidateDraftMutationAsync(
            new InMemoryReadRepository<ApprovalRequest>([pendingFinalApproval]),
            context,
            expectedVersion: 1,
            allowBinaryArtifact: true,
            CancellationToken.None);
        lockedByApproval.IsSuccess.Should().BeFalse();
        string.Join('\n', lockedByApproval.Errors ?? []).Should().Contain("locked after final review");

        artifact.Approve(now.AddMinutes(1));
        artifact.MarkFinal("final/draft/report.html", now.AddMinutes(2));
        workspace.FinalizeWorkspace(now.AddMinutes(2));
        var finalContext = context with { Artifact = artifact, Workspace = workspace };
        var lockedByFinal = await ArtifactWorkspaceP9Policy.ValidateDraftMutationAsync(
            new InMemoryReadRepository<ApprovalRequest>(),
            finalContext,
            expectedVersion: artifact.Version,
            allowBinaryArtifact: true,
            CancellationToken.None);
        lockedByFinal.IsSuccess.Should().BeFalse();
        string.Join('\n', lockedByFinal.Errors ?? []).Should().Contain("Final");

        var files = Array.Empty<ArtifactWorkspaceFileItem>();
        var workspaceDto = ArtifactWorkspaceMapper.Map(workspace, task, files);
        var finalArtifact = workspaceDto.FinalArtifacts.Should().ContainSingle().Which;
        finalArtifact.ArtifactStatus.Should().Be("Final");
        finalArtifact.FinalizedAt.Should().NotBeNull();
        finalArtifact.RelativePath.Should().StartWith("final/");
        finalArtifact.SourceMode.Should().Be("CloudReadonlySandbox");
        finalArtifact.Boundary.Should().Be("SandboxControlledTrial");
        finalArtifact.IsSandbox.Should().BeTrue();
        finalArtifact.SourceLabel.Should().Be("Cloud 只读 Sandbox（非生产）");
        finalArtifact.ResultHash.Should().Be("sandbox-result-hash-001");
        finalArtifact.RowCount.Should().Be(8);
        finalArtifact.IsTruncated.Should().BeTrue();
    }

    private static AgentTask CreateWorkspaceReadyTask(DateTimeOffset now)
    {
        var task = new AgentTask(
            new SessionId(Guid.NewGuid()),
            Guid.NewGuid(),
            "P9 artifact workspace",
            "P9 artifact workspace",
            AgentTaskType.CloudDataReport,
            AgentTaskRiskLevel.Medium,
            null,
            """{"version":1}""",
            now);
        task.AddStep(
            "Generate draft artifact",
            "Generate draft artifact for P9 tests.",
            AgentStepType.ArtifactGeneration,
            "generate_report",
            requiresApproval: false,
            now);
        task.ConfirmExecutablePlan(task.PlanJson, Array.Empty<int>(), now);
        task.ApprovePlan(now);
        return task;
    }

    private static ArtifactWorkspace CreateWorkspace(AgentTask task, string workspaceCode, DateTimeOffset now)
    {
        var workspace = new ArtifactWorkspace(
            task.Id,
            workspaceCode,
            $"/tmp/{workspaceCode}",
            $"/workspaces/{workspaceCode}",
            now);
        task.AttachWorkspace(workspace.Id, now);
        task.MarkWorkspaceReady(now);
        return workspace;
    }

    private sealed class InMemoryArtifactWorkspaceFileStore : IArtifactWorkspaceFileStore
    {
        private readonly Dictionary<string, StoredFile> _files = new(StringComparer.OrdinalIgnoreCase);

        public ArtifactWorkspaceStorageSettings GetSettings()
        {
            return new ArtifactWorkspaceStorageSettings(
                "/tmp/aicopilot-p9",
                ["draft", "final", "versions"],
                ["Markdown", "Html", "Pdf", "Pptx", "Xlsx", "Chart"],
                AllowsUserDefinedPath: false);
        }

        public Task<ArtifactWorkspaceStorageInfo> CreateWorkspaceAsync(
            string workspaceCode,
            Guid taskId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ArtifactWorkspaceStorageInfo($"/tmp/{workspaceCode}", $"/workspaces/{workspaceCode}"));
        }

        public Task<ArtifactFileWriteResult> WriteTextAsync(
            string workspaceCode,
            string relativePath,
            string content,
            string mimeType,
            CancellationToken cancellationToken = default)
        {
            return WriteBytesAsync(workspaceCode, relativePath, Encoding.UTF8.GetBytes(content), mimeType, cancellationToken);
        }

        public Task<ArtifactFileWriteResult> WriteBytesAsync(
            string workspaceCode,
            string relativePath,
            byte[] content,
            string mimeType,
            CancellationToken cancellationToken = default)
        {
            _files[Key(workspaceCode, relativePath)] = new StoredFile(relativePath, content, mimeType, DateTimeOffset.UtcNow);
            return Task.FromResult(new ArtifactFileWriteResult(relativePath, content.LongLength, mimeType));
        }

        public Task<ArtifactFileWriteResult> CopyAsync(
            string workspaceCode,
            string sourceRelativePath,
            string targetRelativePath,
            string mimeType,
            CancellationToken cancellationToken = default)
        {
            var source = _files[Key(workspaceCode, sourceRelativePath)];
            return WriteBytesAsync(workspaceCode, targetRelativePath, source.Content, mimeType, cancellationToken);
        }

        public Task<ArtifactFileReadResult?> OpenReadAsync(
            string workspaceCode,
            string relativePath,
            string mimeType,
            CancellationToken cancellationToken = default)
        {
            if (!_files.TryGetValue(Key(workspaceCode, relativePath), out var file))
            {
                return Task.FromResult<ArtifactFileReadResult?>(null);
            }

            var stream = new MemoryStream(file.Content.ToArray());
            return Task.FromResult<ArtifactFileReadResult?>(new ArtifactFileReadResult(
                stream,
                Path.GetFileName(file.RelativePath),
                file.MimeType,
                file.Content.LongLength));
        }

        public Task<IReadOnlyCollection<ArtifactWorkspaceFileItem>> ListAsync(
            string workspaceCode,
            CancellationToken cancellationToken = default)
        {
            var prefix = workspaceCode + "/";
            var items = _files
                .Where(item => item.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(item => new ArtifactWorkspaceFileItem(
                    Path.GetFileName(item.Value.RelativePath),
                    item.Value.RelativePath,
                    IsDirectory: false,
                    item.Value.Content.LongLength,
                    item.Value.UpdatedAt))
                .ToArray();
            return Task.FromResult<IReadOnlyCollection<ArtifactWorkspaceFileItem>>(items);
        }

        private static string Key(string workspaceCode, string relativePath)
        {
            return workspaceCode + "/" + relativePath.Replace('\\', '/');
        }

        private sealed record StoredFile(string RelativePath, byte[] Content, string MimeType, DateTimeOffset UpdatedAt);
    }
}
