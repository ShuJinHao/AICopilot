using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Specification;

namespace AICopilot.Core.AiGateway.Specifications.Artifacts;

public sealed class ArtifactWorkspaceByCodeSpec : Specification<ArtifactWorkspace>
{
    public ArtifactWorkspaceByCodeSpec(string workspaceCode, bool includeArtifacts = false)
    {
        FilterCondition = workspace => workspace.WorkspaceCode == workspaceCode;
        if (includeArtifacts)
        {
            AddInclude(workspace => workspace.Artifacts);
        }
    }
}

public sealed class ArtifactWorkspaceByTaskSpec : Specification<ArtifactWorkspace>
{
    public ArtifactWorkspaceByTaskSpec(AgentTaskId taskId, bool includeArtifacts = false)
    {
        FilterCondition = workspace => workspace.TaskId == taskId;
        if (includeArtifacts)
        {
            AddInclude(workspace => workspace.Artifacts);
        }
    }
}

public sealed class ArtifactWorkspaceByIdSpec : Specification<ArtifactWorkspace>
{
    public ArtifactWorkspaceByIdSpec(ArtifactWorkspaceId id, bool includeArtifacts = false)
    {
        FilterCondition = workspace => workspace.Id == id;
        if (includeArtifacts)
        {
            AddInclude(workspace => workspace.Artifacts);
        }
    }
}

public sealed class ArtifactWorkspaceByArtifactIdSpec : Specification<ArtifactWorkspace>
{
    public ArtifactWorkspaceByArtifactIdSpec(ArtifactId artifactId)
    {
        FilterCondition = workspace => workspace.Artifacts.Any(artifact => artifact.Id == artifactId);
        AddInclude(workspace => workspace.Artifacts);
    }
}
