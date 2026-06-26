using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Workspaces;

public sealed record ArtifactDto(
    Guid Id,
    string Name,
    string Type,
    string Status,
    string RelativePath,
    long FileSize,
    string MimeType,
    int Version,
    DateTimeOffset UpdatedAt,
    string PreviewKind,
    string DownloadUrl,
    int? GeneratedByStepOrder,
    bool RequiresApproval,
    string ApprovalStatus,
    DateTimeOffset? FinalizedAt,
    int ArtifactVersion,
    string ArtifactStatus,
    string? SourceMode,
    string? Boundary,
    bool IsSimulation,
    bool IsSandbox,
    string? SourceLabel,
    string? QueryHash,
    string? ResultHash,
    int RowCount,
    bool IsTruncated)
{
    public DateTimeOffset CreatedAt { get; init; }

    public int? GeneratedByStep { get; init; }
}

public sealed record ArtifactManifestItemDto(
    Guid ArtifactId,
    string Type,
    string Name,
    string RelativePath,
    string Status,
    int Version,
    int? GeneratedByStep,
    string DownloadUrl,
    DateTimeOffset CreatedAt);

public sealed record ArtifactWorkspaceFileDto(
    string Name,
    string RelativePath,
    bool IsDirectory,
    long FileSize,
    DateTimeOffset UpdatedAt);

public sealed record ArtifactWorkspaceDto(
    Guid Id,
    string WorkspaceCode,
    Guid TaskId,
    string Status,
    IReadOnlyCollection<ArtifactWorkspaceFileDto> Files,
    IReadOnlyCollection<ArtifactDto> Artifacts)
{
    public IReadOnlyCollection<ArtifactManifestItemDto> Manifest { get; init; } = [];

    public IReadOnlyCollection<ArtifactDto> DraftArtifacts { get; init; } = [];

    public IReadOnlyCollection<ArtifactDto> FinalArtifacts { get; init; } = [];
}

public sealed record ArtifactDownloadDto(Stream Stream, string FileName, string MimeType, long FileSize);

public sealed record ArtifactWorkspaceSettingsDto(
    string RootPath,
    IReadOnlyCollection<string> Folders,
    IReadOnlyCollection<string> AllowedArtifactTypes,
    bool AllowsUserDefinedPath);

public sealed record GetArtifactWorkspaceQuery(string Code) : IQuery<Result<ArtifactWorkspaceDto>>;

public sealed record DownloadArtifactQuery(Guid Id) : IQuery<Result<ArtifactDownloadDto>>;

[AuthorizeRequirement("AiGateway.SubmitFinalReview")]
public sealed record SubmitFinalReviewCommand(string Code) : ICommand<Result<ArtifactWorkspaceDto>>;

[AuthorizeRequirement("AiGateway.FinalizeWorkspace")]
public sealed record FinalizeArtifactWorkspaceCommand(string Code) : ICommand<Result<ArtifactWorkspaceDto>>;

[AuthorizeRequirement("AiGateway.GetWorkspace")]
public sealed record GetArtifactWorkspaceSettingsQuery : IQuery<Result<ArtifactWorkspaceSettingsDto>>;

public interface IAgentArtifactWorkspaceService
{
    Task<ArtifactWorkspace> CreateForTaskAsync(AgentTask task, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    Task<Artifact> WriteDraftTextArtifactAsync(
        ArtifactWorkspace workspace,
        ArtifactType artifactType,
        string name,
        string relativePath,
        string content,
        string mimeType,
        AgentStepId? stepId,
        ArtifactSourceMetadata? sourceMetadata,
        CancellationToken cancellationToken);

    Task<Artifact> WriteDraftBinaryArtifactAsync(
        ArtifactWorkspace workspace,
        ArtifactType artifactType,
        string name,
        string relativePath,
        byte[] content,
        string mimeType,
        AgentStepId? stepId,
        ArtifactSourceMetadata? sourceMetadata,
        CancellationToken cancellationToken);
}
