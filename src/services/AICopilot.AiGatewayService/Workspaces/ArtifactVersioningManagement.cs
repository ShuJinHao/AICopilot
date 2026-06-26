using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Workspaces;

public sealed record ArtifactContentDto(
    Guid Id,
    string WorkspaceCode,
    string Name,
    string Type,
    string Status,
    string RelativePath,
    int Version,
    string MimeType,
    string Content,
    DateTimeOffset UpdatedAt,
    bool Editable);

public sealed record UpdateArtifactContentRequest(string Content, int ExpectedVersion, string? Comment = null);

public sealed record ArtifactVersionDto(
    int Version,
    string FileName,
    long FileSize,
    string MimeType,
    string Sha256,
    DateTimeOffset CreatedAt,
    bool IsCurrent,
    string DownloadUrl);

public sealed record ArtifactTextDiffEntryDto(
    string Kind,
    int? OldLine,
    int? NewLine,
    string? OldText,
    string? NewText);

public sealed record ArtifactTextDiffDto(
    Guid ArtifactId,
    int FromVersion,
    int ToVersion,
    int FromLineCount,
    int ToLineCount,
    IReadOnlyCollection<ArtifactTextDiffEntryDto> Entries,
    bool Truncated);

public sealed record RestoreArtifactVersionRequest(int ExpectedVersion, string? Comment = null);

[AuthorizeRequirement("AiGateway.GetWorkspace")]
public sealed record GetArtifactContentQuery(Guid Id) : IQuery<Result<ArtifactContentDto>>;

[AuthorizeRequirement("AiGateway.EditArtifact")]
public sealed record UpdateArtifactContentCommand(Guid Id, string Content, int ExpectedVersion, string? Comment)
    : ICommand<Result<ArtifactWorkspaceDto>>;

[AuthorizeRequirement("AiGateway.GetWorkspace")]
public sealed record GetArtifactVersionsQuery(Guid Id) : IQuery<Result<IReadOnlyCollection<ArtifactVersionDto>>>;

[AuthorizeRequirement("AiGateway.DownloadArtifact")]
public sealed record DownloadArtifactVersionQuery(Guid Id, int Version) : IQuery<Result<ArtifactDownloadDto>>;

[AuthorizeRequirement("AiGateway.GetWorkspace")]
public sealed record GetArtifactVersionDiffQuery(Guid Id, int FromVersion, int ToVersion)
    : IQuery<Result<ArtifactTextDiffDto>>;

[AuthorizeRequirement("AiGateway.EditArtifact")]
public sealed record RestoreArtifactVersionCommand(Guid Id, int Version, int ExpectedVersion, string? Comment)
    : ICommand<Result<ArtifactWorkspaceDto>>;
