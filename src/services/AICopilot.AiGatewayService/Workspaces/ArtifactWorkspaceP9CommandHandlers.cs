using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Workspaces;

public sealed class CreateArtifactRevisionCommentCommandHandler(
    ArtifactWorkspaceP9Coordinator artifactWorkspaceP9Coordinator)
    : ICommandHandler<CreateArtifactRevisionCommentCommand, Result<ArtifactRevisionCommentDto>>
{
    public Task<Result<ArtifactRevisionCommentDto>> Handle(
        CreateArtifactRevisionCommentCommand request,
        CancellationToken cancellationToken) =>
        artifactWorkspaceP9Coordinator.CreateRevisionCommentAsync(request, cancellationToken);
}

public sealed class RegenerateDraftArtifactCommandHandler(
    ArtifactWorkspaceP9Coordinator artifactWorkspaceP9Coordinator)
    : ICommandHandler<RegenerateDraftArtifactCommand, Result<ArtifactWorkspaceDto>>
{
    public Task<Result<ArtifactWorkspaceDto>> Handle(
        RegenerateDraftArtifactCommand request,
        CancellationToken cancellationToken) =>
        artifactWorkspaceP9Coordinator.RegenerateDraftAsync(request, cancellationToken);
}

public sealed class SubmitArtifactForFinalApprovalCommandHandler(
    ArtifactWorkspaceP9Coordinator artifactWorkspaceP9Coordinator)
    : ICommandHandler<SubmitArtifactForFinalApprovalCommand, Result<ArtifactWorkspaceDto>>
{
    public Task<Result<ArtifactWorkspaceDto>> Handle(
        SubmitArtifactForFinalApprovalCommand request,
        CancellationToken cancellationToken) =>
        artifactWorkspaceP9Coordinator.SubmitForFinalApprovalAsync(request, cancellationToken);
}
