using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Workspaces;

public sealed class GetArtifactWorkspaceSettingsQueryHandler(IArtifactWorkspaceFileStore fileStore)
    : IQueryHandler<GetArtifactWorkspaceSettingsQuery, Result<ArtifactWorkspaceSettingsDto>>
{
    public Task<Result<ArtifactWorkspaceSettingsDto>> Handle(
        GetArtifactWorkspaceSettingsQuery request,
        CancellationToken cancellationToken)
    {
        var settings = fileStore.GetSettings();
        return Task.FromResult(Result.Success(new ArtifactWorkspaceSettingsDto(
            settings.RootPath,
            settings.Folders,
            settings.AllowedArtifactTypes,
            settings.AllowsUserDefinedPath)));
    }
}

public sealed class GetArtifactWorkspaceQueryHandler(
    ArtifactWorkspaceQueryCoordinator workspaceQueryCoordinator)
    : IQueryHandler<GetArtifactWorkspaceQuery, Result<ArtifactWorkspaceDto>>
{
    public Task<Result<ArtifactWorkspaceDto>> Handle(
        GetArtifactWorkspaceQuery request,
        CancellationToken cancellationToken) =>
        workspaceQueryCoordinator.GetAsync(request, cancellationToken);
}

public sealed class DownloadArtifactQueryHandler(
    ArtifactWorkspaceQueryCoordinator workspaceQueryCoordinator)
    : IQueryHandler<DownloadArtifactQuery, Result<ArtifactDownloadDto>>
{
    public Task<Result<ArtifactDownloadDto>> Handle(
        DownloadArtifactQuery request,
        CancellationToken cancellationToken) =>
        workspaceQueryCoordinator.DownloadAsync(request, cancellationToken);
}
