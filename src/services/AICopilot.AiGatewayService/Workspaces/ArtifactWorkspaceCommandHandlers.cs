using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Workspaces;

public sealed class SubmitFinalReviewCommandHandler(
    ArtifactWorkspaceLifecycleCoordinator workspaceLifecycleCoordinator)
    : ICommandHandler<SubmitFinalReviewCommand, Result<ArtifactWorkspaceDto>>
{
    public Task<Result<ArtifactWorkspaceDto>> Handle(
        SubmitFinalReviewCommand request,
        CancellationToken cancellationToken) =>
        workspaceLifecycleCoordinator.SubmitFinalReviewAsync(request.Code, cancellationToken);
}

public sealed class FinalizeArtifactWorkspaceCommandHandler(
    ArtifactWorkspaceLifecycleCoordinator workspaceLifecycleCoordinator)
    : ICommandHandler<FinalizeArtifactWorkspaceCommand, Result<ArtifactWorkspaceDto>>
{
    public Task<Result<ArtifactWorkspaceDto>> Handle(
        FinalizeArtifactWorkspaceCommand request,
        CancellationToken cancellationToken) =>
        workspaceLifecycleCoordinator.FinalizeAsync(request.Code, cancellationToken);
}
