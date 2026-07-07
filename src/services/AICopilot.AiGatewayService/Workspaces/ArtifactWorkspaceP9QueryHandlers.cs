using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Workspaces;

public sealed class GetAgentArtifactPreviewQueryHandler(
    ArtifactWorkspaceP9Coordinator artifactWorkspaceP9Coordinator)
    : IQueryHandler<GetAgentArtifactPreviewQuery, Result<AgentArtifactPreviewDto>>
{
    public Task<Result<AgentArtifactPreviewDto>> Handle(
        GetAgentArtifactPreviewQuery request,
        CancellationToken cancellationToken) =>
        artifactWorkspaceP9Coordinator.GetPreviewAsync(request, cancellationToken);
}
