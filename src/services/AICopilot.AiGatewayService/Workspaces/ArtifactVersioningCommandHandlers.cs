using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Workspaces;

public sealed class UpdateArtifactContentCommandHandler(
    ArtifactVersioningCommandCoordinator versioningCommandCoordinator)
    : ICommandHandler<UpdateArtifactContentCommand, Result<ArtifactWorkspaceDto>>
{
    public Task<Result<ArtifactWorkspaceDto>> Handle(
        UpdateArtifactContentCommand request,
        CancellationToken cancellationToken) =>
        versioningCommandCoordinator.UpdateContentAsync(request, cancellationToken);
}

public sealed class RestoreArtifactVersionCommandHandler(
    ArtifactVersioningCommandCoordinator versioningCommandCoordinator)
    : ICommandHandler<RestoreArtifactVersionCommand, Result<ArtifactWorkspaceDto>>
{
    public Task<Result<ArtifactWorkspaceDto>> Handle(
        RestoreArtifactVersionCommand request,
        CancellationToken cancellationToken) =>
        versioningCommandCoordinator.RestoreVersionAsync(request, cancellationToken);
}
