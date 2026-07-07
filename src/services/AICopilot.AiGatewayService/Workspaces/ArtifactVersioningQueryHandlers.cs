using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Workspaces;

public sealed class GetArtifactContentQueryHandler(
    ArtifactVersioningQueryCoordinator versioningQueryCoordinator)
    : IQueryHandler<GetArtifactContentQuery, Result<ArtifactContentDto>>
{
    public Task<Result<ArtifactContentDto>> Handle(
        GetArtifactContentQuery request,
        CancellationToken cancellationToken) =>
        versioningQueryCoordinator.GetContentAsync(request, cancellationToken);
}

public sealed class GetArtifactVersionsQueryHandler(
    ArtifactVersioningQueryCoordinator versioningQueryCoordinator)
    : IQueryHandler<GetArtifactVersionsQuery, Result<IReadOnlyCollection<ArtifactVersionDto>>>
{
    public Task<Result<IReadOnlyCollection<ArtifactVersionDto>>> Handle(
        GetArtifactVersionsQuery request,
        CancellationToken cancellationToken) =>
        versioningQueryCoordinator.GetVersionsAsync(request, cancellationToken);
}

public sealed class DownloadArtifactVersionQueryHandler(
    ArtifactVersioningQueryCoordinator versioningQueryCoordinator)
    : IQueryHandler<DownloadArtifactVersionQuery, Result<ArtifactDownloadDto>>
{
    public Task<Result<ArtifactDownloadDto>> Handle(
        DownloadArtifactVersionQuery request,
        CancellationToken cancellationToken) =>
        versioningQueryCoordinator.DownloadVersionAsync(request, cancellationToken);
}

public sealed class GetArtifactVersionDiffQueryHandler(
    ArtifactVersioningQueryCoordinator versioningQueryCoordinator)
    : IQueryHandler<GetArtifactVersionDiffQuery, Result<ArtifactTextDiffDto>>
{
    public Task<Result<ArtifactTextDiffDto>> Handle(
        GetArtifactVersionDiffQuery request,
        CancellationToken cancellationToken) =>
        versioningQueryCoordinator.GetDiffAsync(request, cancellationToken);
}
