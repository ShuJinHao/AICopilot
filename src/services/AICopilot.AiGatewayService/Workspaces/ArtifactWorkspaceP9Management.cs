using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Workspaces;

public sealed record AgentArtifactPreviewDto(
    Guid ArtifactId,
    string Name,
    string ArtifactType,
    string PreviewKind,
    string ArtifactStatus,
    int ArtifactVersion,
    string RelativePath,
    long FileSize,
    string MimeType,
    string? SourceMode,
    string? Boundary,
    bool IsSimulation,
    bool IsSandbox,
    string? SourceLabel,
    string? QueryHash,
    string? ResultHash,
    int RowCount,
    bool IsTruncated,
    string? Content,
    IReadOnlyCollection<string> Columns,
    IReadOnlyCollection<IReadOnlyDictionary<string, string>> Rows,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record ArtifactRevisionCommentDto(
    Guid ArtifactId,
    int ArtifactVersion,
    string CommentHash,
    DateTimeOffset CreatedAt);

public sealed record RegenerateDraftArtifactRequest(string Content, int ExpectedVersion, string? Comment = null);

public sealed record CreateArtifactRevisionCommentRequest(string Comment, int ExpectedVersion);

[AuthorizeRequirement("AiGateway.GetWorkspace")]
public sealed record GetAgentArtifactPreviewQuery(Guid Id) : IQuery<Result<AgentArtifactPreviewDto>>;

[AuthorizeRequirement("AiGateway.EditArtifact")]
public sealed record CreateArtifactRevisionCommentCommand(Guid Id, string Comment, int ExpectedVersion)
    : ICommand<Result<ArtifactRevisionCommentDto>>;

[AuthorizeRequirement("AiGateway.EditArtifact")]
public sealed record RegenerateDraftArtifactCommand(Guid Id, string Content, int ExpectedVersion, string? Comment)
    : ICommand<Result<ArtifactWorkspaceDto>>;

[AuthorizeRequirement("AiGateway.SubmitFinalReview")]
public sealed record SubmitArtifactForFinalApprovalCommand(Guid Id) : ICommand<Result<ArtifactWorkspaceDto>>;
