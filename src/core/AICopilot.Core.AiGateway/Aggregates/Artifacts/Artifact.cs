using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.Artifacts;

public sealed class Artifact : IEntity<ArtifactId>
{
    private Artifact()
    {
    }

    internal Artifact(
        ArtifactWorkspaceId workspaceId,
        AgentTaskId taskId,
        ArtifactType artifactType,
        string name,
        string relativePath,
        long fileSize,
        string mimeType,
        AgentStepId? createdByStepId,
        DateTimeOffset nowUtc)
    {
        if (fileSize < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fileSize), "Artifact file size cannot be negative.");
        }

        Id = ArtifactId.New();
        WorkspaceId = workspaceId;
        TaskId = taskId;
        ArtifactType = artifactType;
        Name = NormalizeRequired(name, nameof(name), 200);
        RelativePath = ArtifactPathGuard.NormalizeDraftPath(relativePath);
        FileSize = fileSize;
        MimeType = NormalizeRequired(mimeType, nameof(mimeType), 200);
        Version = 1;
        Status = ArtifactStatus.Draft;
        CreatedByStepId = createdByStepId;
        CreatedAt = nowUtc;
        UpdatedAt = nowUtc;
    }

    public ArtifactId Id { get; private set; }

    public ArtifactWorkspaceId WorkspaceId { get; private set; }

    public AgentTaskId TaskId { get; private set; }

    public ArtifactType ArtifactType { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string RelativePath { get; private set; } = string.Empty;

    public long FileSize { get; private set; }

    public string MimeType { get; private set; } = string.Empty;

    public int Version { get; private set; }

    public ArtifactStatus Status { get; private set; }

    public string? SourceMode { get; private set; }

    public string? Boundary { get; private set; }

    public bool IsSimulation { get; private set; }

    public bool IsSandbox { get; private set; }

    public string? SourceLabel { get; private set; }

    public string? QueryHash { get; private set; }

    public string? ResultHash { get; private set; }

    public string? EvidenceSetDigest { get; private set; }

    public int RowCount { get; private set; }

    public bool IsTruncated { get; private set; }

    public AgentStepId? CreatedByStepId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? FinalizedAt { get; private set; }

    public void MarkReviewing(DateTimeOffset nowUtc)
    {
        EnsureStatus(ArtifactStatus.Draft);
        Status = ArtifactStatus.Reviewing;
        UpdatedAt = nowUtc;
    }

    public void Approve(DateTimeOffset nowUtc)
    {
        if (Status is not ArtifactStatus.Draft and not ArtifactStatus.Reviewing)
        {
            throw new InvalidOperationException("Only draft or reviewing artifacts can be approved.");
        }

        Status = ArtifactStatus.Approved;
        UpdatedAt = nowUtc;
    }

    public void MarkFinal(string finalRelativePath, DateTimeOffset nowUtc)
    {
        EnsureStatus(ArtifactStatus.Approved);
        RelativePath = ArtifactPathGuard.NormalizeFinalPath(finalRelativePath);
        Status = ArtifactStatus.Final;
        UpdatedAt = nowUtc;
        FinalizedAt = nowUtc;
    }

    public void AddVersion(string draftRelativePath, long fileSize, DateTimeOffset nowUtc)
    {
        if (Status == ArtifactStatus.Final)
        {
            throw new InvalidOperationException("Final artifacts cannot receive draft versions.");
        }

        if (fileSize < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fileSize), "Artifact file size cannot be negative.");
        }

        RelativePath = ArtifactPathGuard.NormalizeDraftPath(draftRelativePath);
        FileSize = fileSize;
        Version++;
        Status = ArtifactStatus.Draft;
        UpdatedAt = nowUtc;
        FinalizedAt = null;
    }

    public void ApplySourceMetadata(ArtifactSourceMetadata? metadata)
    {
        if (metadata is null)
        {
            return;
        }

        SourceMode = NormalizeOptional(metadata.SourceMode, 80);
        Boundary = NormalizeOptional(metadata.Boundary, 120);
        IsSimulation = metadata.IsSimulation;
        IsSandbox = metadata.IsSandbox;
        SourceLabel = NormalizeOptional(metadata.SourceLabel, 200);
        QueryHash = NormalizeOptional(metadata.QueryHash, 128);
        ResultHash = NormalizeOptional(metadata.ResultHash, 128);
        EvidenceSetDigest = NormalizeSha256(metadata.EvidenceSetDigest, nameof(metadata.EvidenceSetDigest));
        RowCount = Math.Max(0, metadata.RowCount);
        IsTruncated = metadata.IsTruncated;
    }

    private void EnsureStatus(ArtifactStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException($"Artifact must be {expected}.");
        }
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

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is null
            ? null
            : normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string? NormalizeSha256(string? value, string paramName)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized is null)
        {
            return null;
        }

        if (normalized.Length != 64 || normalized.Any(character =>
                character is not (>= '0' and <= '9') &&
                character is not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException($"{paramName} must be a lowercase SHA-256 digest.", paramName);
        }

        return normalized;
    }
}

public sealed record ArtifactSourceMetadata(
    string? SourceMode,
    string? Boundary,
    bool IsSimulation,
    bool IsSandbox,
    string? SourceLabel,
    string? QueryHash,
    string? ResultHash,
    int RowCount,
    bool IsTruncated,
    string? EvidenceSetDigest = null);
