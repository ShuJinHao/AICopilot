using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.Artifacts;

public sealed class ArtifactWorkspace : BaseEntity<ArtifactWorkspaceId>, IAggregateRoot<ArtifactWorkspaceId>
{
    private readonly List<Artifact> _artifacts = [];

    private ArtifactWorkspace()
    {
    }

    public ArtifactWorkspace(
        AgentTaskId taskId,
        string workspaceCode,
        string rootPath,
        string workspaceUrl,
        DateTimeOffset nowUtc)
    {
        Id = ArtifactWorkspaceId.New();
        TaskId = taskId;
        WorkspaceCode = NormalizeCode(workspaceCode);
        RootPath = NormalizeRequired(rootPath, nameof(rootPath), 1000);
        WorkspaceUrl = NormalizeRequired(workspaceUrl, nameof(workspaceUrl), 1000);
        Status = ArtifactWorkspaceStatus.Active;
        CreatedAt = nowUtc;
        UpdatedAt = nowUtc;
    }

    public AgentTaskId TaskId { get; private set; }

    public string WorkspaceCode { get; private set; } = string.Empty;

    public string RootPath { get; private set; } = string.Empty;

    public string WorkspaceUrl { get; private set; } = string.Empty;

    public ArtifactWorkspaceStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<Artifact> Artifacts => _artifacts.AsReadOnly();

    public Artifact AddDraftArtifact(
        ArtifactType artifactType,
        string name,
        string relativePath,
        long fileSize,
        string mimeType,
        AgentStepId? createdByStepId,
        DateTimeOffset nowUtc)
    {
        if (Status == ArtifactWorkspaceStatus.Finalized)
        {
            throw new InvalidOperationException("artifact_finalized: Finalized workspaces cannot receive draft artifacts.");
        }

        if (Status is not ArtifactWorkspaceStatus.Active and not ArtifactWorkspaceStatus.Draft)
        {
            throw new InvalidOperationException("Artifacts can only be added to active or draft workspaces.");
        }

        var artifact = new Artifact(Id, TaskId, artifactType, name, relativePath, fileSize, mimeType, createdByStepId, nowUtc);
        _artifacts.Add(artifact);
        UpdatedAt = nowUtc;
        return artifact;
    }

    public void FinalizeWorkspace(DateTimeOffset nowUtc)
    {
        if (_artifacts.Any(artifact => artifact.Status != ArtifactStatus.Final))
        {
            throw new InvalidOperationException("Workspace can only be finalized after every artifact is final.");
        }

        Status = ArtifactWorkspaceStatus.Finalized;
        UpdatedAt = nowUtc;
    }

    private static string NormalizeCode(string workspaceCode)
    {
        var code = NormalizeRequired(workspaceCode, nameof(workspaceCode), 100);
        if (!code.StartsWith("ws_", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Workspace code must start with ws_.", nameof(workspaceCode));
        }

        return code;
    }

    private static string NormalizeRequired(string value, string paramName, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException($"{paramName} is required.", paramName);
        }

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}
